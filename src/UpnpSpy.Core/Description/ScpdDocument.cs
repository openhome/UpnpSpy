using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.Description;

/// <summary>
/// Parsed SCPD (Service Control Protocol Description) document per UDA 1.0 §2.2.
/// Argument order inside each <see cref="ActionDefinition"/> is preserved from
/// the SCPD because the SOAP envelope built in US7 must list arguments in
/// SCPD-declared order (UDA 1.0 §3.1.1).
/// </summary>
public sealed record ScpdDocument(
    IReadOnlyList<ActionDefinition> Actions,
    IReadOnlyList<StateVariableDefinition> StateVariables);
