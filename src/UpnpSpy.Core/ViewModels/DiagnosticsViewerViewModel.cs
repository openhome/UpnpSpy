using CommunityToolkit.Mvvm.ComponentModel;
using UpnpSpy.Core.Collections;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Drives the Diagnostics window (FR-041). Primes <see cref="Entries"/> from the
/// ring buffer snapshot on open, then live-tails subsequent records via
/// <see cref="IDiagnosticBuffer.Subscribe"/>. Each raw <see cref="DiagnosticEntry"/>
/// is projected (snapshot-at-arrival) into a <see cref="DiagnosticEntryRow"/> that
/// resolves the affiliated device's identity and endpoint from the
/// <see cref="DeviceRegistry"/> so the viewer can show a recognisable name and
/// IP alongside the message. Live updates are marshalled onto the UI thread
/// through <see cref="IDispatcher"/>. Disposing unsubscribes so closing the
/// window stops further mutations.
/// </summary>
public sealed partial class DiagnosticsViewerViewModel : ObservableObject, IDisposable
{
    public const int Capacity = 5_000;

    /// <summary>Rendered when a diagnostic entry has no affiliated device or endpoint.</summary>
    public const string Placeholder = "—";

    private readonly IDiagnosticBuffer _buffer;
    private readonly DeviceRegistry _registry;
    private readonly IDispatcher _dispatcher;
    private IDisposable? _subscription;
    private bool _disposed;

    public BoundedObservableCollection<DiagnosticEntryRow> Entries { get; }

    public DiagnosticsViewerViewModel(IDiagnosticBuffer buffer, DeviceRegistry registry, IDispatcher dispatcher)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Entries = new BoundedObservableCollection<DiagnosticEntryRow>(Math.Max(buffer.Capacity, 1));
    }

    /// <summary>
    /// Primes <see cref="Entries"/> with the current snapshot, then attaches a
    /// live observer for subsequent records. Safe to call once per instance.
    /// </summary>
    public void Start()
    {
        if (_subscription is not null) throw new InvalidOperationException("Viewer already started.");

        foreach (var entry in _buffer.Snapshot())
            Entries.Add(BuildRow(entry));

        _subscription = _buffer.Subscribe(new Observer(this));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscription?.Dispose();
        _subscription = null;
    }

    private void OnEntry(DiagnosticEntry entry) =>
        _dispatcher.Post(() => Entries.Add(BuildRow(entry)));

    private DiagnosticEntryRow BuildRow(DiagnosticEntry entry) => new(
        entry,
        identity: ResolveIdentity(entry),
        endpoint: ResolveEndpoint(entry));

    private string ResolveIdentity(DiagnosticEntry entry)
    {
        if (!entry.Context.TryGetValue("device.uuid", out var uuid) || string.IsNullOrWhiteSpace(uuid))
            return Placeholder;

        if (_registry.Snapshot().TryGetValue(uuid, out var device)
            && !string.IsNullOrWhiteSpace(device.FriendlyName))
        {
            return device.FriendlyName!;
        }

        return "uuid:" + uuid;
    }

    private static string ResolveEndpoint(DiagnosticEntry entry)
    {
        if (entry.Context.TryGetValue("url", out var url) && !string.IsNullOrWhiteSpace(url))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            {
                return uri.IsDefaultPort
                    ? uri.Host
                    : $"{uri.Host}:{uri.Port}";
            }
            return url;
        }

        if (entry.Context.TryGetValue("remote.endpoint", out var ep) && !string.IsNullOrWhiteSpace(ep))
            return ep;

        return Placeholder;
    }

    private sealed class Observer : IObserver<DiagnosticEntry>
    {
        private readonly DiagnosticsViewerViewModel _owner;
        public Observer(DiagnosticsViewerViewModel owner) { _owner = owner; }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(DiagnosticEntry value) => _owner.OnEntry(value);
    }
}

/// <summary>
/// Display projection of one <see cref="DiagnosticEntry"/> for the Diagnostics
/// viewer. Carries the original entry plus a friendly-name-or-uuid
/// <see cref="Identity"/> and a host:port <see cref="Endpoint"/> resolved at
/// the moment the row enters the viewer's collection (snapshot-at-arrival).
/// Pass-through properties on the timestamp / severity / category / message
/// keep XAML <c>x:Bind</c> bindings and tests terse.
/// </summary>
public sealed class DiagnosticEntryRow
{
    public DiagnosticEntry Entry { get; }
    public string Identity { get; }
    public string Endpoint { get; }

    public DiagnosticEntryRow(DiagnosticEntry entry, string identity, string endpoint)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
    }

    public DateTimeOffset Timestamp => Entry.Timestamp;
    public DiagnosticSeverity Severity => Entry.Severity;
    public string Category => Entry.Category;
    public string Message => Entry.Message;
}
