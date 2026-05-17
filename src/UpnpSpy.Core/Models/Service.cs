namespace UpnpSpy.Core.Models;

public sealed class Service
{
    public required string OwningDeviceUuid { get; init; }
    public required string ContainingDeviceUdn { get; init; }
    public string? ContainingDeviceFriendlyName { get; init; }
    public required string ServiceId { get; init; }
    public required string ServiceType { get; init; }
    public required Uri ScpdUrl { get; init; }
    public required Uri ControlUrl { get; init; }
    public required Uri EventSubUrl { get; init; }

    public FetchState ScpdFetchState { get; set; } = FetchState.NotFetched;
    public string? ScpdFetchError { get; set; }
    public IReadOnlyList<ActionDefinition> Actions { get; set; } = Array.Empty<ActionDefinition>();
    public IReadOnlyList<StateVariableDefinition> StateVariables { get; set; } = Array.Empty<StateVariableDefinition>();

    public string Label
    {
        get
        {
            var typeLabel = ServiceTypeTail();
            var rootUdn = "uuid:" + OwningDeviceUuid;
            if (string.Equals(ContainingDeviceUdn, rootUdn, StringComparison.Ordinal))
                return typeLabel;
            var prefix = !string.IsNullOrWhiteSpace(ContainingDeviceFriendlyName)
                ? ContainingDeviceFriendlyName!
                : ContainingDeviceUdn;
            return $"{prefix} · {typeLabel}";
        }
    }

    private string ServiceTypeTail()
    {
        const string Marker = ":service:";
        var idx = ServiceType.IndexOf(Marker, StringComparison.Ordinal);
        if (idx >= 0)
            return ServiceType[(idx + Marker.Length)..];
        return ServiceId;
    }
}
