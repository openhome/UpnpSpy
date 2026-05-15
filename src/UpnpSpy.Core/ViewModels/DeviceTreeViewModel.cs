using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Mirrors the <see cref="DeviceRegistry"/> into a UI-bindable collection.
/// FR-047: a device appears in <see cref="Devices"/> if and only if its
/// <see cref="Device.DescriptionFetchState"/> is <see cref="FetchState.Loaded"/>.
/// Devices in NotFetched / Fetching / Failed states are deliberately absent
/// from the tree; their failures (Failed) remain recorded as Warning
/// <c>DiagnosticEntry</c> items so the user can find them via View → Diagnostics.
/// </summary>
public partial class DeviceTreeViewModel : ObservableObject
{
    private readonly DeviceRegistry _registry;
    private readonly IDispatcher _dispatcher;
    private readonly Func<Models.Device, DeviceNodeViewModel> _createNode;

    public ObservableCollection<DeviceNodeViewModel> Devices { get; } = new();

    /// <summary>
    /// Bare-bones constructor used by US1 tests — produces nodes that are
    /// label-only (no lazy expansion). Production composition uses the
    /// three-arg overload that wires in <see cref="DeviceNodeFactory"/>.
    /// </summary>
    public DeviceTreeViewModel(DeviceRegistry registry, IDispatcher dispatcher)
        : this(registry, dispatcher, d => new DeviceNodeViewModel(d))
    {
    }

    public DeviceTreeViewModel(DeviceRegistry registry, IDispatcher dispatcher, DeviceNodeFactory nodeFactory)
        : this(registry, dispatcher, (nodeFactory ?? throw new ArgumentNullException(nameof(nodeFactory))).Create)
    {
    }

    private DeviceTreeViewModel(DeviceRegistry registry, IDispatcher dispatcher, Func<Models.Device, DeviceNodeViewModel> createNode)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _createNode = createNode;

        foreach (var device in _registry.Snapshot().Values)
        {
            if (device.DescriptionFetchState == FetchState.Loaded)
                Devices.Add(_createNode(device));
        }

        _registry.DeviceAdded += OnAdded;
        _registry.DeviceUpdated += OnUpdated;
        _registry.DeviceRemoved += OnRemoved;
    }

    private void OnAdded(DeviceAddedEvent e)
    {
        // FR-047: a freshly-added device has not yet been fetched, so it does
        // not yet belong in the tree. The promotion happens on DeviceUpdated
        // once the eager dispatcher transitions the state to Loaded.
        if (e.Device.DescriptionFetchState != FetchState.Loaded) return;

        _dispatcher.Post(() =>
        {
            for (var i = 0; i < Devices.Count; i++)
            {
                if (Devices[i].Device.Uuid == e.Device.Uuid) return;
            }
            Devices.Add(_createNode(e.Device));
        });
    }

    private void OnUpdated(DeviceUpdatedEvent e)
    {
        _dispatcher.Post(() =>
        {
            for (var i = 0; i < Devices.Count; i++)
            {
                if (Devices[i].Device.Uuid == e.Device.Uuid)
                {
                    Devices[i].RefreshLabel();
                    return;
                }
            }
            // FR-047 promotion: not-yet-visible device whose eager fetch just
            // completed successfully — add it to the tree now.
            if (e.Device.DescriptionFetchState == FetchState.Loaded)
                Devices.Add(_createNode(e.Device));
        });
    }

    private void OnRemoved(DeviceRemovedEvent e)
    {
        _dispatcher.Post(() =>
        {
            for (var i = Devices.Count - 1; i >= 0; i--)
            {
                if (Devices[i].Device.Uuid == e.Uuid)
                {
                    Devices.RemoveAt(i);
                    return;
                }
            }
        });
    }
}
