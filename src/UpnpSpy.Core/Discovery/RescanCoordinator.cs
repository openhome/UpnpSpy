using Microsoft.Extensions.Logging;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;
using UpnpSpy.Core.Ssdp;

namespace UpnpSpy.Core.Discovery;

/// <summary>
/// Manages at-most-one active <see cref="DiscoverySession"/> on behalf of the
/// View &gt; Rescan command (FR-021–FR-024). Each invocation snapshots the
/// registry, issues a fresh M-SEARCH burst, tracks the UUIDs heard during the
/// MX + grace window, and at the deadline removes any previously-known UUID
/// that did not respond. A rescan started while another is running marks the
/// older session <see cref="DiscoverySessionState.Superseded"/> so its pruning
/// step is skipped (data-model §7, spec Edge Case).
/// </summary>
public sealed class RescanCoordinator
{
    private static readonly TimeSpan Mx = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(1);

    private readonly ISsdpTransport _transport;
    private readonly DeviceRegistry _registry;
    private readonly DiscoveryService _discovery;
    private readonly IClock _clock;
    private readonly ILogger<RescanCoordinator> _logger;
    private readonly object _gate = new();
    private DiscoverySession? _activeSession;

    public RescanCoordinator(
        ISsdpTransport transport,
        DeviceRegistry registry,
        DiscoveryService discovery,
        IClock clock,
        ILogger<RescanCoordinator> logger)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RescanAsync(CancellationToken cancellationToken)
    {
        var session = StartSession();

        void OnHeard(string uuid) => session.HeardThisSession.Add(uuid);
        _discovery.DeviceHeard += OnHeard;

        try
        {
            // ST=upnp:rootdevice mirrors the startup probe (DiscoveryService) — UDA
            // 1.0 §1.3.3 yields one response per root device, so embedded children
            // and services do not produce duplicate registry entries.
            await _transport.SendMSearchAsync("upnp:rootdevice", Mx, cancellationToken).ConfigureAwait(false);
            await Task.Delay(Mx + Grace, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutdown / supersession */ }
        finally
        {
            _discovery.DeviceHeard -= OnHeard;
            CompleteAndPrune(session);
        }
    }

    private DiscoverySession StartSession()
    {
        lock (_gate)
        {
            if (_activeSession is { State: DiscoverySessionState.Running } previous)
            {
                previous.State = DiscoverySessionState.Superseded;
                _logger.LogInformation("Rescan superseded a session in progress.");
            }

            var started = _clock.UtcNow;
            var session = new DiscoverySession
            {
                StartedUtc = started,
                Deadline = started + Mx + Grace,
                IsStartupSession = false,
                KnownAtStart = new HashSet<string>(_registry.Uuids(), StringComparer.Ordinal),
            };
            _activeSession = session;
            return session;
        }
    }

    private void CompleteAndPrune(DiscoverySession session)
    {
        lock (_gate)
        {
            if (session.State == DiscoverySessionState.Superseded) return;
            session.State = DiscoverySessionState.Completed;
        }

        foreach (var uuid in session.KnownAtStart)
        {
            if (!session.HeardThisSession.Contains(uuid))
                _registry.Remove(uuid);
        }
    }
}
