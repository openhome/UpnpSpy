namespace UpnpSpy.Core.Platform;

/// <summary>
/// FR-048: enumerates eligible adapters once at construction and lets the
/// user (via the View menu) pick one. Default selection is the first
/// eligible adapter, or <c>null</c> if there are none. Thread-safe.
/// </summary>
public sealed class NetworkAdapterSelector : INetworkAdapterSelector
{
    private readonly object _gate = new();
    private EligibleInterface? _selected;

    public NetworkAdapterSelector(INetworkInterfaceEnumerator enumerator)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        Available = enumerator.EnumerateEligible();
        _selected = Available.Count > 0 ? Available[0] : null;
    }

    public IReadOnlyList<EligibleInterface> Available { get; }

    public EligibleInterface? Selected
    {
        get { lock (_gate) return _selected; }
    }

    public event Action<AdapterSelectionChanged>? Changed;

    public Task SelectAsync(EligibleInterface adapter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Available.Any(a => Matches(a, adapter)))
            throw new ArgumentException(
                $"Adapter '{adapter.Name}' is not in the available list.", nameof(adapter));

        EligibleInterface? previous;
        EligibleInterface current;
        lock (_gate)
        {
            previous = _selected;
            if (previous is not null && Matches(previous, adapter))
                return Task.CompletedTask;
            _selected = adapter;
            current = adapter;
        }

        Changed?.Invoke(new AdapterSelectionChanged(previous, current));
        return Task.CompletedTask;
    }

    private static bool Matches(EligibleInterface a, EligibleInterface b) =>
        string.Equals(a.Name, b.Name, StringComparison.Ordinal)
        && a.Ipv4Address.Equals(b.Ipv4Address);
}
