using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// Builds one <see cref="DevicePropertiesViewModel"/> per popup.
/// </summary>
public sealed class DevicePropertiesPopupFactory
{
    private readonly DeviceRegistry _registry;

    public DevicePropertiesPopupFactory(DeviceRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public DevicePropertiesViewModel Create(Device device) => new(device, _registry);
}
