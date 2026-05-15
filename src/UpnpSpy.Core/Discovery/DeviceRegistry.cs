using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.Discovery;

/// <summary>
/// UUID-keyed registry of live UPnP root devices. Thread-safe.
/// Exposes typed events for added / updated / removed transitions so view-models
/// can mirror state without polling.
/// </summary>
public sealed class DeviceRegistry
{
    private readonly Dictionary<string, Device> _devices = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public event Action<DeviceAddedEvent>? DeviceAdded;
    public event Action<DeviceUpdatedEvent>? DeviceUpdated;
    public event Action<DeviceRemovedEvent>? DeviceRemoved;

    public IReadOnlyDictionary<string, Device> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, Device>(_devices, StringComparer.Ordinal);
        }
    }

    public bool Contains(string uuid)
    {
        lock (_gate) return _devices.ContainsKey(uuid);
    }

    public IReadOnlyList<string> Uuids()
    {
        lock (_gate) return _devices.Keys.ToArray();
    }

    /// <summary>
    /// Inserts or merges a device. Returns the canonical Device instance held by the
    /// registry (the input candidate's mutable fields are merged onto it if it
    /// already existed).
    /// </summary>
    public Device TryAddOrUpdate(Device candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        bool added;
        bool updated;
        Device canonical;

        lock (_gate)
        {
            if (_devices.TryGetValue(candidate.Uuid, out var existing))
            {
                added = false;
                updated = MergeInto(existing, candidate);
                canonical = existing;
            }
            else
            {
                _devices[candidate.Uuid] = candidate;
                added = true;
                updated = false;
                canonical = candidate;
            }
        }

        if (added)
            DeviceAdded?.Invoke(new DeviceAddedEvent(canonical));
        else if (updated)
            DeviceUpdated?.Invoke(new DeviceUpdatedEvent(canonical));

        return canonical;
    }

    /// <summary>
    /// Broadcasts <see cref="DeviceUpdated"/> for the canonical device with the
    /// given UUID. No-op if the UUID is not currently in the registry. The
    /// <see cref="EagerDescriptionDispatcher"/> calls this after writing
    /// description-derived fields (FriendlyName / Services / DescriptionFetchState)
    /// onto the canonical device, so subscribers (tree view-models, the
    /// per-node state-machine waiting on the Fetching → Loaded/Failed
    /// transition) can react (FR-043).
    /// </summary>
    public void NotifyUpdated(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(uuid));

        Device? canonical;
        lock (_gate)
        {
            _devices.TryGetValue(uuid, out canonical);
        }

        if (canonical is not null)
            DeviceUpdated?.Invoke(new DeviceUpdatedEvent(canonical));
    }

    /// <summary>
    /// FR-050: removes every device from the registry, raising one
    /// <see cref="DeviceRemoved"/> per UUID so subscribers (the tree view-model,
    /// any open popups via FR-037) can react. Used by the adapter-switch
    /// orchestration in <c>ShellViewModel</c>.
    /// </summary>
    public void Clear()
    {
        string[] uuids;
        lock (_gate)
        {
            uuids = _devices.Keys.ToArray();
            _devices.Clear();
        }

        foreach (var uuid in uuids)
            DeviceRemoved?.Invoke(new DeviceRemovedEvent(uuid));
    }

    public bool Remove(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(uuid));

        bool removed;
        lock (_gate)
        {
            removed = _devices.Remove(uuid);
        }

        if (removed)
            DeviceRemoved?.Invoke(new DeviceRemovedEvent(uuid));

        return removed;
    }

    /// <summary>
    /// Merges candidate's mutable fields onto existing. Returns true if any visible
    /// field changed (currently: FriendlyName). LastSeenUtc is bumped silently to
    /// avoid update churn on every alive packet — that level of refresh is the
    /// registry's bookkeeping and is not part of the user-visible flicker surface.
    /// </summary>
    private static bool MergeInto(Device existing, Device candidate)
    {
        var visibleChanged = false;

        if (!string.IsNullOrWhiteSpace(candidate.FriendlyName)
            && !string.Equals(existing.FriendlyName, candidate.FriendlyName, StringComparison.Ordinal))
        {
            existing.FriendlyName = candidate.FriendlyName;
            visibleChanged = true;
        }

        if (candidate.LastSeenUtc > existing.LastSeenUtc)
            existing.LastSeenUtc = candidate.LastSeenUtc;

        existing.AliveCount++;
        existing.LocationUrl = candidate.LocationUrl;

        if (candidate.ServerHeader is not null) existing.ServerHeader = candidate.ServerHeader;
        if (candidate.CacheControlMaxAge is not null) existing.CacheControlMaxAge = candidate.CacheControlMaxAge;
        if (candidate.BootId is not null) existing.BootId = candidate.BootId;
        if (candidate.ConfigId is not null) existing.ConfigId = candidate.ConfigId;

        if (candidate.ObservedOnInterfaces.Count > 0)
        {
            var combined = new HashSet<string>(existing.ObservedOnInterfaces, StringComparer.Ordinal);
            foreach (var nic in candidate.ObservedOnInterfaces)
                combined.Add(nic);
            existing.ObservedOnInterfaces = combined;
        }

        return visibleChanged;
    }
}
