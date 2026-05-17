namespace UpnpSpy.Core.Models;

public sealed record ArgumentDefinition(
    string Name,
    ArgumentDirection Direction,
    string RelatedStateVariable,
    string? DataType);
