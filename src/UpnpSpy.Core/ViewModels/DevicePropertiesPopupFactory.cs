using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Builds one <see cref="DevicePropertiesViewModel"/> per popup.
/// </summary>
public sealed class DevicePropertiesPopupFactory
{
    private readonly DeviceRegistry _registry;
    private readonly IDispatcher _dispatcher;

    public DevicePropertiesPopupFactory(DeviceRegistry registry, IDispatcher dispatcher)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public DevicePropertiesViewModel Create(Device device) => new(device, _registry, _dispatcher);
}
