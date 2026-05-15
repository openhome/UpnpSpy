using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.Control;

/// <summary>
/// Invokes a SOAP action on a UPnP service's control URL (UDA 1.0 §3.1).
/// Implementations must map every documented outcome onto <see cref="InvocationResult"/>
/// without throwing, except for <see cref="OperationCanceledException"/> from the
/// caller's cancellation token.
/// </summary>
public interface IControlClient
{
    Task<InvocationResult> InvokeAsync(
        Service service,
        ActionDefinition action,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken);
}
