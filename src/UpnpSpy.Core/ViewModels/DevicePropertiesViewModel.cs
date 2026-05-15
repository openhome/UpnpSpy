using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using UpnpSpy.Core.Description;
using UpnpSpy.Core.Discovery;
using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.ViewModels;

/// <summary>
/// FR-052: read-only view-model behind the device Properties window. Surfaces
/// every metadata field the application has captured for one
/// <see cref="Device"/>, with <see cref="Placeholder"/> in place of any field
/// the device did not declare so the rendered grid is unambiguous. Watches
/// <see cref="DeviceRegistry"/> so the window can show a "device no longer
/// reachable" banner per FR-037 if the user keeps it open after byebye.
/// </summary>
public partial class DevicePropertiesViewModel : ObservableObject, IDisposable
{
    /// <summary>Rendered in place of any field the device did not declare.</summary>
    public const string Placeholder = "—";

    private readonly DeviceRegistry _registry;
    private bool _disposed;

    public Device Device { get; }
    public string Title { get; }

    public string FriendlyName { get; }
    public string DeviceType { get; }
    public string Uuid { get; }
    public string PresentationUrlText { get; }
    public Uri? PresentationUrl { get; }

    public string Manufacturer { get; }
    public string ManufacturerUrlText { get; }
    public Uri? ManufacturerUrl { get; }
    public string ModelName { get; }
    public string ModelNumber { get; }
    public string ModelDescription { get; }
    public string ModelUrlText { get; }
    public Uri? ModelUrl { get; }
    public string SerialNumber { get; }
    public string Upc { get; }

    public string LocationUrlText { get; }
    public Uri LocationUrl { get; }
    public string Endpoint { get; }
    public string ServerHeader { get; }
    public string CacheControlMaxAge { get; }

    public string FirstSeenUtc { get; }
    public string LastSeenUtc { get; }
    public string AliveCount { get; }
    public string BootId { get; }
    public string ConfigId { get; }

    public IReadOnlyList<EmbeddedDeviceSummary> EmbeddedDevices { get; }

    [ObservableProperty]
    private bool _isDeviceUnreachable;

    public DevicePropertiesViewModel(Device device, DeviceRegistry registry)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        Title = $"Properties · {device.Label}";

        FriendlyName = device.Label;
        DeviceType = Or(device.DeviceType);
        Uuid = device.Uuid;
        PresentationUrl = device.PresentationUrl;
        PresentationUrlText = device.PresentationUrl?.ToString() ?? Placeholder;

        Manufacturer = Or(device.Manufacturer);
        ManufacturerUrl = device.ManufacturerUrl;
        ManufacturerUrlText = device.ManufacturerUrl?.ToString() ?? Placeholder;
        ModelName = Or(device.ModelName);
        ModelNumber = Or(device.ModelNumber);
        ModelDescription = Or(device.ModelDescription);
        ModelUrl = device.ModelUrl;
        ModelUrlText = device.ModelUrl?.ToString() ?? Placeholder;
        SerialNumber = Or(device.SerialNumber);
        Upc = Or(device.Upc);

        LocationUrl = device.LocationUrl;
        LocationUrlText = device.LocationUrl.ToString();
        Endpoint = $"{device.LocationUrl.Host}:{device.LocationUrl.Port}";
        ServerHeader = Or(device.ServerHeader);
        CacheControlMaxAge = device.CacheControlMaxAge is int max
            ? $"{max.ToString(CultureInfo.InvariantCulture)} s"
            : Placeholder;

        FirstSeenUtc = device.FirstSeenUtc == default
            ? Placeholder
            : device.FirstSeenUtc.ToLocalTime().ToString("u", CultureInfo.InvariantCulture);
        LastSeenUtc = device.LastSeenUtc == default
            ? Placeholder
            : device.LastSeenUtc.ToLocalTime().ToString("u", CultureInfo.InvariantCulture);
        AliveCount = device.AliveCount.ToString(CultureInfo.InvariantCulture);
        BootId = device.BootId?.ToString(CultureInfo.InvariantCulture) ?? Placeholder;
        ConfigId = device.ConfigId?.ToString(CultureInfo.InvariantCulture) ?? Placeholder;

        EmbeddedDevices = device.EmbeddedDevices;

        _registry.DeviceRemoved += OnDeviceRemoved;
    }

    private void OnDeviceRemoved(DeviceRemovedEvent evt)
    {
        if (string.Equals(evt.Uuid, Device.Uuid, StringComparison.Ordinal))
            IsDeviceUnreachable = true;
    }

    private static string Or(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Placeholder : value;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _registry.DeviceRemoved -= OnDeviceRemoved;
    }
}
