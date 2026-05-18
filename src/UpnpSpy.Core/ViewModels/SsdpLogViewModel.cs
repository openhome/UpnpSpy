using CommunityToolkit.Mvvm.ComponentModel;
using UpnpSpy.Core.Collections;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Right-pane SSDP advertisement log. Entries land at index 0 (newest-first per
/// FR-055) on the UI thread via <see cref="IDispatcher"/> so the underlying
/// ObservableCollection is only ever mutated from a single thread, as WinUI
/// bindings require. FR-016 eviction discards the tail (the oldest row), not
/// the head.
/// </summary>
public sealed partial class SsdpLogViewModel : ObservableObject
{
    public const int Capacity = 10_000;

    private readonly IDispatcher _dispatcher;

    public BoundedObservableCollection<SsdpLogEntry> Entries { get; }

    public SsdpLogViewModel(IDispatcher dispatcher)
        : this(dispatcher, Capacity)
    {
    }

    public SsdpLogViewModel(IDispatcher dispatcher, int capacity)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Entries = new BoundedObservableCollection<SsdpLogEntry>(capacity, BoundedEvictionMode.EvictTail);
    }

    public void Append(SsdpLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _dispatcher.Post(() => Entries.Insert(0, entry));
    }
}
