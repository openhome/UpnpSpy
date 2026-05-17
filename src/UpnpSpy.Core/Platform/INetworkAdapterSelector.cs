namespace UpnpSpy.Core.Platform;

/// <summary>
/// FR-048: holds the currently-selected IPv4 network adapter. The app
/// operates on exactly one adapter at a time; this singleton is the single
/// source of truth for "which NIC are we on right now." Changing the
/// selection raises <see cref="Changed"/> so <c>ShellViewModel</c> can
/// orchestrate the rebind sequence (FR-050).
/// </summary>
public interface INetworkAdapterSelector
{
    IReadOnlyList<EligibleInterface> Available { get; }

    EligibleInterface? Selected { get; }

    /// <summary>
    /// Selects the given adapter. No-op if <paramref name="adapter"/> is
    /// already selected. Throws <see cref="ArgumentException"/> if the
    /// adapter is not in <see cref="Available"/>.
    /// </summary>
    Task SelectAsync(EligibleInterface adapter, CancellationToken cancellationToken);

    event Action<AdapterSelectionChanged>? Changed;
}

public sealed record AdapterSelectionChanged(EligibleInterface? Previous, EligibleInterface? Current);
