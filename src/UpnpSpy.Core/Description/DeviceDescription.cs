namespace UpnpSpy.Core.Description;

/// <summary>
/// Parsed root-device description. <see cref="Services"/> is the flattened
/// union of the root's <c>&lt;serviceList&gt;</c> and every <c>&lt;serviceList&gt;</c>
/// encountered while walking nested <c>&lt;deviceList&gt;</c> elements
/// recursively (research §20). The remaining fields are the UDA 1.0 §2.3
/// "device description" elements; all except <c>DeviceType</c> are marked
/// optional in UDA so they may be <c>null</c>.
/// </summary>
public sealed record DeviceDescription(
    string Uuid,
    string? FriendlyName,
    string DeviceType,
    string? Manufacturer,
    Uri? ManufacturerUrl,
    string? ModelName,
    string? ModelDescription,
    string? ModelNumber,
    Uri? ModelUrl,
    string? SerialNumber,
    string? Upc,
    Uri? PresentationUrl,
    IReadOnlyList<ServiceDescriptor> Services,
    IReadOnlyList<EmbeddedDeviceSummary> EmbeddedDevices)
{
    /// <summary>
    /// Convenience factory for tests / call sites that only care about
    /// identity + services and don't want to spell out every optional field.
    /// </summary>
    public static DeviceDescription Minimum(
        string uuid,
        string? friendlyName = null,
        string deviceType = "",
        IReadOnlyList<ServiceDescriptor>? services = null,
        IReadOnlyList<EmbeddedDeviceSummary>? embeddedDevices = null) =>
        new(uuid, friendlyName, deviceType,
            Manufacturer: null, ManufacturerUrl: null,
            ModelName: null, ModelDescription: null, ModelNumber: null,
            ModelUrl: null, SerialNumber: null, Upc: null, PresentationUrl: null,
            Services: services ?? Array.Empty<ServiceDescriptor>(),
            EmbeddedDevices: embeddedDevices ?? Array.Empty<EmbeddedDeviceSummary>());
}

/// <summary>
/// Lightweight summary of an embedded <c>&lt;device&gt;</c> child for the
/// Properties window (FR-052). Recursive — embedded devices can themselves
/// contain embedded devices.
/// </summary>
public sealed record EmbeddedDeviceSummary(
    string Udn,
    string DeviceType,
    string? FriendlyName,
    IReadOnlyList<EmbeddedDeviceSummary> EmbeddedDevices);
