namespace UpnpSpy.Core.Description;

/// <summary>
/// One service as it appeared in the device description XML, annotated with
/// the immediate containing <c>&lt;device&gt;</c>'s UDN/friendly name so the
/// view-model can prefix the label of services declared by embedded children
/// (research §20).
/// </summary>
public sealed record ServiceDescriptor(
    string ContainingDeviceUdn,
    string? ContainingDeviceFriendlyName,
    string ServiceId,
    string ServiceType,
    Uri ScpdUrl,
    Uri ControlUrl,
    Uri EventSubUrl);
