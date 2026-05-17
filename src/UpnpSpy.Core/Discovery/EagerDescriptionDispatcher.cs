using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using UpnpSpy.Core.Description;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Lifecycle;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.Discovery;

/// <summary>
/// Eagerly fetches a device's description as soon as it enters the registry
/// (FR-043). Subscribes to <see cref="DeviceRegistry.DeviceAdded"/>, gates
/// concurrent fetches through a shared <see cref="SemaphoreSlim"/> sized at
/// <see cref="MaxConcurrentFetches"/>, and writes the parsed FriendlyName /
/// Services back onto the canonical <see cref="Device"/> before calling
/// <see cref="DeviceRegistry.NotifyUpdated"/> so subscribers (the tree
/// view-model's label binding, the per-node expansion state-machine) can react.
/// Per-device fetches are tied to a linked <see cref="CancellationTokenSource"/>;
/// a <see cref="DeviceRegistry.DeviceRemoved"/> event (byebye / rescan-prune)
/// cancels any in-flight fetch for the affected UUID.
/// </summary>
public sealed class EagerDescriptionDispatcher : IDisposable
{
    /// <summary>
    /// Cap on concurrent description fetches across all devices. Sized to match
    /// the plan's "bounded HTTP fan-out" constraint (research §9, plan
    /// Constraints) — eight in-flight fetches is enough to clear a small LAN
    /// burst within SC-001's friendly-name budget without saturating the host
    /// or the upstream link.
    /// </summary>
    public const int MaxConcurrentFetches = 8;

    private readonly DeviceRegistry _registry;
    private readonly IDeviceDescriptionFetcher _fetcher;
    private readonly IClock _clock;
    private readonly IDiagnosticSink? _diagnostics;
    private readonly ILogger<EagerDescriptionDispatcher> _logger;
    private readonly AppShutdownTokenSource _shutdown;
    private readonly SemaphoreSlim _gate;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlight
        = new(StringComparer.Ordinal);
    private bool _started;
    private bool _disposed;

    public EagerDescriptionDispatcher(
        DeviceRegistry registry,
        IDeviceDescriptionFetcher fetcher,
        IClock clock,
        AppShutdownTokenSource shutdown,
        ILogger<EagerDescriptionDispatcher> logger,
        IDiagnosticSink? diagnostics = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _diagnostics = diagnostics;
        _gate = new SemaphoreSlim(MaxConcurrentFetches, MaxConcurrentFetches);
    }

    /// <summary>
    /// Subscribes to registry events and back-fills any devices already in the
    /// registry. Idempotent; safe to call after the SSDP receive pump has
    /// started.
    /// </summary>
    public void Start()
    {
        if (_started) return;
        _started = true;

        _registry.DeviceAdded += OnDeviceAdded;
        _registry.DeviceRemoved += OnDeviceRemoved;

        // Back-fill for the (rare) case where a device entered the registry
        // between MainWindow open and ShellViewModel.InitializeAsync.
        foreach (var device in _registry.Snapshot().Values)
            EnqueueIfFresh(device);
    }

    private void OnDeviceAdded(DeviceAddedEvent e) => EnqueueIfFresh(e.Device);

    private void EnqueueIfFresh(Device device)
    {
        if (device.DescriptionFetchState != FetchState.NotFetched) return;

        device.DescriptionFetchState = FetchState.Fetching;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);

        // If a stale CTS is somehow still in the dict (race with a previous
        // Remove that hasn't fired yet), cancel it so we don't leak.
        if (_inFlight.TryGetValue(device.Uuid, out var stale))
        {
            try { stale.Cancel(); stale.Dispose(); } catch { }
        }
        _inFlight[device.Uuid] = cts;

        _ = Task.Run(() => FetchAndApplyAsync(device, cts));
    }

    private void OnDeviceRemoved(DeviceRemovedEvent e)
    {
        if (_inFlight.TryRemove(e.Uuid, out var cts))
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* completed concurrently */ }
        }
    }

    private async Task FetchAndApplyAsync(Device device, CancellationTokenSource cts)
    {
        var gateAcquired = false;
        try
        {
            await _gate.WaitAsync(cts.Token).ConfigureAwait(false);
            gateAcquired = true;

            var result = await _fetcher.FetchAsync(device.LocationUrl, cts.Token).ConfigureAwait(false);
            ApplyResult(device, result);
        }
        catch (OperationCanceledException)
        {
            // Device left the registry mid-fetch (or app is shutting down); the
            // Device object is no longer observable to any view-model, so leave
            // it where it is — no NotifyUpdated, no diagnostic.
        }
        catch (Exception ex)
        {
            ApplyResult(device, new DeviceDescriptionFetchResult.TransportError(ex.Message, ex));
        }
        finally
        {
            if (gateAcquired)
                _gate.Release();

            // Only remove our own entry; a race with Remove could already have
            // taken it out.
            _inFlight.TryRemove(new KeyValuePair<string, CancellationTokenSource>(device.Uuid, cts));
            cts.Dispose();
        }
    }

    private void ApplyResult(Device device, DeviceDescriptionFetchResult result)
    {
        switch (result)
        {
            case DeviceDescriptionFetchResult.Success success:
                // Backstop for the SSDP-layer NT=upnp:rootdevice filter: if a
                // device was registered with UUID X but the description at its
                // LOCATION declares root UDN Y ≠ X, then X is an embedded child
                // (or a non-conformant device) — drop it from the registry rather
                // than misleadingly stamp the root's friendly name onto it.
                if (!string.Equals(device.Uuid, success.Description.Uuid, StringComparison.Ordinal))
                {
                    DropAsEmbedded(device, success.Description.Uuid);
                    return;
                }
                ApplySuccess(device, success.Description);
                break;
            case DeviceDescriptionFetchResult.HttpError http:
                ApplyFailure(device, "Description.Fetch",
                    $"HTTP {http.StatusCode} {http.ReasonPhrase}", http.StatusCode, null);
                break;
            case DeviceDescriptionFetchResult.TransportError tx:
                ApplyFailure(device, "Description.Fetch", tx.Message, null, tx.Underlying);
                break;
            case DeviceDescriptionFetchResult.ParseError pe:
                ApplyFailure(device, "Description.Parse", pe.Message, null, null);
                break;
        }

        // Fire DeviceUpdated for both success and failure: the state transition
        // (Fetching → Loaded/Failed) is what the per-node expansion state-machine
        // is waiting on, regardless of whether the label changed.
        _registry.NotifyUpdated(device.Uuid);
    }

    private void DropAsEmbedded(Device device, string declaredRootUuid)
    {
        if (_diagnostics is not null)
        {
            _diagnostics.Record(new DiagnosticEntry(
                Timestamp: _clock.UtcNow,
                Severity: DiagnosticSeverity.Information,
                Category: "Description.Fetch",
                Message: "Description declared a different root UDN; treating UUID as embedded and removing from registry.",
                Context: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["device.uuid"] = device.Uuid,
                    ["url"] = device.LocationUrl.AbsoluteUri,
                    ["declared.root.uuid"] = declaredRootUuid,
                },
                Exception: null));
        }
        else
        {
            _logger.LogInformation(
                "Removing {Uuid} from registry: description at {Url} declared root UDN {RootUuid}.",
                device.Uuid, device.LocationUrl, declaredRootUuid);
        }

        // Remove fires DeviceRemoved → OnDeviceRemoved → cancels and disposes
        // any in-flight CTS for this UUID. The finally block in
        // FetchAndApplyAsync handles the now-no-op cleanup safely
        // (TryRemove returns false; cts.Dispose is idempotent).
        _registry.Remove(device.Uuid);
    }

    private static void ApplySuccess(Device device, DeviceDescription description)
    {
        if (!string.IsNullOrWhiteSpace(description.FriendlyName))
            device.FriendlyName = description.FriendlyName;

        device.DeviceType = string.IsNullOrWhiteSpace(description.DeviceType) ? null : description.DeviceType;
        device.Manufacturer = description.Manufacturer;
        device.ManufacturerUrl = description.ManufacturerUrl;
        device.ModelName = description.ModelName;
        device.ModelDescription = description.ModelDescription;
        device.ModelNumber = description.ModelNumber;
        device.ModelUrl = description.ModelUrl;
        device.SerialNumber = description.SerialNumber;
        device.Upc = description.Upc;
        device.PresentationUrl = description.PresentationUrl;
        device.EmbeddedDevices = description.EmbeddedDevices;

        device.Services = description.Services
            .Select(d => MapToService(device.Uuid, d))
            .ToArray();
        device.DescriptionFetchState = FetchState.Loaded;
        device.DescriptionFetchError = null;
    }

    private void ApplyFailure(Device device, string category, string message, int? httpStatus, Exception? exception)
    {
        device.DescriptionFetchState = FetchState.Failed;
        device.DescriptionFetchError = message;
        device.Services = Array.Empty<Service>();

        if (_diagnostics is null)
        {
            _logger.LogWarning("Eager description fetch failed for {Uuid}: {Message}", device.Uuid, message);
            return;
        }

        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["device.uuid"] = device.Uuid,
            ["url"] = device.LocationUrl.AbsoluteUri,
        };
        if (httpStatus is not null)
            context["http.status"] = httpStatus.Value.ToString(CultureInfo.InvariantCulture);

        _diagnostics.Record(new DiagnosticEntry(
            Timestamp: _clock.UtcNow,
            Severity: DiagnosticSeverity.Warning,
            Category: category,
            Message: $"Eager description fetch failed: {message}",
            Context: context,
            Exception: exception?.ToString()));
    }

    private static Service MapToService(string rootUuid, ServiceDescriptor d) => new()
    {
        OwningDeviceUuid = rootUuid,
        ContainingDeviceUdn = d.ContainingDeviceUdn,
        ContainingDeviceFriendlyName = d.ContainingDeviceFriendlyName,
        ServiceId = d.ServiceId,
        ServiceType = d.ServiceType,
        ScpdUrl = d.ScpdUrl,
        ControlUrl = d.ControlUrl,
        EventSubUrl = d.EventSubUrl,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registry.DeviceAdded -= OnDeviceAdded;
        _registry.DeviceRemoved -= OnDeviceRemoved;

        foreach (var kvp in _inFlight)
        {
            try { kvp.Value.Cancel(); kvp.Value.Dispose(); }
            catch { /* shutdown */ }
        }
        _inFlight.Clear();
        _gate.Dispose();
    }
}
