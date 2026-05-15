namespace UpnpSpy.Core.Models;

public sealed record ActionDefinition(
    string Name,
    IReadOnlyList<ArgumentDefinition> Inputs,
    IReadOnlyList<ArgumentDefinition> Outputs);
