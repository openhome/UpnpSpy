using System.Collections.ObjectModel;

namespace UpnpSpy.Core.Collections;

/// <summary>
/// Direction in which a <see cref="BoundedObservableCollection{T}"/> evicts when
/// it reaches capacity.
/// </summary>
public enum BoundedEvictionMode
{
    /// <summary>
    /// Evict index 0 (the head). Suits chronological collections where new
    /// items are appended at the tail — the head is therefore the oldest.
    /// Default for backward compatibility.
    /// </summary>
    EvictHead,

    /// <summary>
    /// Evict <c>Count - 1</c> (the tail). Suits newest-first collections where
    /// new items are inserted at index 0 — the tail is therefore the oldest.
    /// </summary>
    EvictTail,
}

/// <summary>
/// ObservableCollection that evicts an end item once <c>Count</c> reaches
/// <see cref="Capacity"/>. The end that is evicted is controlled by
/// <see cref="EvictionMode"/>: callers that append chronologically use
/// <see cref="BoundedEvictionMode.EvictHead"/> (default); callers that insert
/// at index 0 (newest-first, e.g. the subscription popup's event list — FR-033)
/// use <see cref="BoundedEvictionMode.EvictTail"/>. All eviction goes through
/// the standard INotifyCollectionChanged contract so bindings see consistent
/// Remove + Add notifications rather than silent loss.
/// </summary>
public sealed class BoundedObservableCollection<T> : ObservableCollection<T>
{
    public BoundedObservableCollection(int capacity)
        : this(capacity, BoundedEvictionMode.EvictHead)
    {
    }

    public BoundedObservableCollection(int capacity, BoundedEvictionMode evictionMode)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        Capacity = capacity;
        EvictionMode = evictionMode;
    }

    public int Capacity { get; }

    public BoundedEvictionMode EvictionMode { get; }

    protected override void InsertItem(int index, T item)
    {
        if (Count >= Capacity)
        {
            // Evict oldest first so listeners observe the removal before the insertion.
            var evictAt = EvictionMode == BoundedEvictionMode.EvictHead ? 0 : Count - 1;
            RemoveItem(evictAt);

            // The eviction may have shifted the requested insertion index out of range.
            if (index > Count) index = Count;
        }
        base.InsertItem(index, item);
    }
}
