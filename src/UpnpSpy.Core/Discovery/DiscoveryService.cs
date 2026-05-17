using Microsoft.Extensions.Logging;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;
using UpnpSpy.Core.Ssdp;
using UpnpSpy.Core.ViewModels;

namespace UpnpSpy.Core.Discovery;

/// <summary>
/// Orchestrates SSDP discovery: owns the single receive pump (consuming
/// <see cref="ISsdpTransport.ReceivedMessages"/>) and exposes
/// <see cref="RunStartupDiscoveryAsync"/> to trigger an M-SEARCH burst.
/// The pump runs continuously between <see cref="StartAsync"/> and disposal,
/// so it processes both M-SEARCH responses (during a discovery window) and
/// unsolicited NOTIFY ssdp:alive/ssdp:byebye datagrams (outside any window).
/// Devices arrive via the registry's event stream.
/// </summary>
public sealed class DiscoveryService : IAsyncDisposable
{
    private static readonly TimeSpan StartupMx = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(1);

    private readonly ISsdpTransport _transport;
    private readonly DeviceRegistry _registry;
    private readonly SsdpMessageParser _parser;
    private readonly IClock _clock;
    private readonly ILogger<DiscoveryService> _logger;
    private readonly IDiagnosticSink? _diagnostics;
    private readonly SsdpLogViewModel? _ssdpLog;
    private readonly CancellationTokenSource _pumpCts = new();
    private readonly object _gate = new();
    private Task? _pumpTask;
    private bool _started;
    private bool _disposed;

    public DiscoveryService(
        ISsdpTransport transport,
        DeviceRegistry registry,
        SsdpMessageParser parser,
        IClock clock,
        ILogger<DiscoveryService> logger,
        IDiagnosticSink? diagnostics = null,
        SsdpLogViewModel? ssdpLog = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _diagnostics = diagnostics;
        _ssdpLog = ssdpLog;
    }

    /// <summary>
    /// Raised once per received alive NOTIFY / M-SEARCH response, carrying the
    /// bare UUID of the responding device. Lets a <see cref="RescanCoordinator"/>
    /// build the "heard this session" set without polluting registry events.
    /// </summary>
    public event Action<string>? DeviceHeard;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }

        await _transport.StartAsync(cancellationToken).ConfigureAwait(false);
        _pumpTask = Task.Run(() => PumpAsync(_pumpCts.Token));
    }

    public async Task RunStartupDiscoveryAsync(CancellationToken cancellationToken)
    {
        // ST=upnp:rootdevice — UDA 1.0 §1.3.3 guarantees one response per root device,
        // so we don't ingest the per-embedded / per-service responses that ssdp:all
        // would produce (which would otherwise create duplicate registry entries
        // for IGD-style chassis like Sky's router).
        await _transport.SendMSearchAsync("upnp:rootdevice", StartupMx, cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Delay(StartupMx + StartupGrace, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var datagram in _transport.ReceivedMessages.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                try { ProcessDatagram(datagram); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to process SSDP datagram from {Interface}", datagram.InterfaceName); }
            }
        }
        catch (OperationCanceledException) { /* clean shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSDP receive pump terminated unexpectedly");
        }
    }

    private void ProcessDatagram(ReceivedSsdpDatagram datagram)
    {
        var parsed = _parser.Parse(datagram.Payload);
        if (parsed is null)
        {
            // Category=Ssdp.Parse so the diagnostic viewer surfaces these distinctly
            // from other discovery-pump warnings (User Story 4 acceptance #4).
            EmitParseWarning(datagram);
            return;
        }

        switch (parsed)
        {
            case SsdpNotifyMessage notify:
                HandleNotify(notify, datagram);
                break;
            case SsdpSearchResponse response:
                HandleSearchResponse(response, datagram);
                break;
        }
    }

    private void HandleNotify(SsdpNotifyMessage notify, ReceivedSsdpDatagram datagram)
    {
        if (notify.Nts.Equals("ssdp:byebye", StringComparison.OrdinalIgnoreCase))
        {
            // FR-014/FR-015: log every alive/byebye regardless of NT.
            AppendLog(datagram, SsdpKind.Byebye, notify.Uuid, notify.Nt);

            // Only roots are tracked in the registry (spec Assumptions:
            // "Embedded devices ... are not shown as separate tree entries").
            // A byebye for an embedded device's UDN or a service NT is for an
            // entity we never registered — drop it.
            if (IsRootDeviceAdvertisement(notify.Nt))
                _registry.Remove(notify.Uuid);
            return;
        }

        if (!notify.Nts.Equals("ssdp:alive", StringComparison.OrdinalIgnoreCase))
            return;

        AppendLog(datagram, SsdpKind.Alive, notify.Uuid, notify.Nt);

        // Per spec Assumptions: only root devices materialise as tree entries.
        // Each chassis emits one alive per (root|embedded device|service); the
        // root one carries NT: upnp:rootdevice and is the canonical signal.
        if (!IsRootDeviceAdvertisement(notify.Nt))
            return;

        if (notify.Location is null)
            return;

        _registry.TryAddOrUpdate(NewDevice(notify.Uuid, notify.Location, datagram,
            serverHeader: notify.Server,
            cacheControlMaxAge: notify.CacheControlMaxAge,
            bootId: notify.BootId,
            configId: notify.ConfigId));
        DeviceHeard?.Invoke(notify.Uuid);
    }

    private static bool IsRootDeviceAdvertisement(string nt) =>
        string.Equals(nt, "upnp:rootdevice", StringComparison.OrdinalIgnoreCase);

    private void AppendLog(ReceivedSsdpDatagram datagram, SsdpKind kind, string uuid, string nt)
    {
        if (_ssdpLog is null) return;
        _ssdpLog.Append(new SsdpLogEntry(
            ReceivedUtc: datagram.ReceivedUtc,
            Kind: kind,
            DeviceUuid: uuid,
            Nt: nt,
            SourceInterfaceName: datagram.InterfaceName));
    }

    private void EmitParseWarning(ReceivedSsdpDatagram datagram)
    {
        if (_diagnostics is null)
        {
            _logger.LogWarning("Dropped unparseable SSDP datagram from {Interface}", datagram.InterfaceName);
            return;
        }

        _diagnostics.Record(new DiagnosticEntry(
            Timestamp: _clock.UtcNow,
            Severity: DiagnosticSeverity.Warning,
            Category: "Ssdp.Parse",
            Message: "Dropped unparseable SSDP datagram.",
            Context: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["interface"] = datagram.InterfaceName,
                ["remote.endpoint"] = datagram.RemoteEndpoint.ToString(),
                ["payload.bytes"] = datagram.Payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            Exception: null));
    }

    private void HandleSearchResponse(SsdpSearchResponse response, ReceivedSsdpDatagram datagram)
    {
        _registry.TryAddOrUpdate(NewDevice(response.Uuid, response.Location, datagram,
            serverHeader: response.Server,
            cacheControlMaxAge: response.CacheControlMaxAge,
            bootId: null,
            configId: null));
        DeviceHeard?.Invoke(response.Uuid);
    }

    private static Device NewDevice(
        string uuid,
        Uri locationUrl,
        ReceivedSsdpDatagram datagram,
        string? serverHeader,
        int? cacheControlMaxAge,
        int? bootId,
        int? configId) => new()
    {
        Uuid = uuid,
        LocationUrl = locationUrl,
        FirstSeenUtc = datagram.ReceivedUtc,
        LastSeenUtc = datagram.ReceivedUtc,
        AliveCount = 1,
        ObservedOnInterfaces = new HashSet<string>(StringComparer.Ordinal) { datagram.InterfaceName },
        ServerHeader = serverHeader,
        CacheControlMaxAge = cacheControlMaxAge,
        BootId = bootId,
        ConfigId = configId,
    };

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _pumpCts.Cancel();
        if (_pumpTask is not null)
        {
            try { await _pumpTask.ConfigureAwait(false); }
            catch { /* shutdown */ }
        }
        _pumpCts.Dispose();
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
