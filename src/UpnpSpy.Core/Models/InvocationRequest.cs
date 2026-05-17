namespace UpnpSpy.Core.Models;

public sealed record InvocationRequest(
    Service Service,
    ActionDefinition Action,
    IReadOnlyDictionary<string, string> Inputs,
    DateTimeOffset SubmittedUtc,
    CancellationToken Cancellation);
