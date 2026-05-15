namespace UpnpSpy.Core.Models;

public sealed record StateVariableDefinition(
    string Name,
    string DataType,
    bool SendsEvents,
    IReadOnlyList<string>? AllowedValues);
