using UpnpSpy.Core.Control;
using UpnpSpy.Core.Models;

namespace UpnpSpy.Tests.Control;

/// <summary>
/// Test double for <see cref="IControlClient"/>. Returns canned
/// <see cref="InvocationResult"/>s matched by a <c>(service, action)</c>
/// predicate; records every invocation's inputs verbatim.
/// </summary>
internal sealed class FakeControlClient : IControlClient
{
    private readonly List<(Func<Service, ActionDefinition, bool> Predicate, InvocationResult Result)> _matchers = new();

    public InvocationResult Default { get; set; } =
        new InvocationResult.TransportError(
            "no canned result",
            null,
            new DateTimeOffset(2026, 5, 13, 0, 0, 0, TimeSpan.Zero));

    public List<RecordedCall> Calls { get; } = new();

    public TaskCompletionSource<bool>? CompletionGate { get; set; }

    public void SetResult(string actionName, InvocationResult result) =>
        _matchers.Add(((_, action) => string.Equals(action.Name, actionName, StringComparison.Ordinal), result));

    public void SetResult(Func<Service, ActionDefinition, bool> predicate, InvocationResult result) =>
        _matchers.Add((predicate, result));

    public async Task<InvocationResult> InvokeAsync(
        Service service,
        ActionDefinition action,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken)
    {
        Calls.Add(new RecordedCall(service, action, new Dictionary<string, string>(inputs, StringComparer.Ordinal)));

        if (CompletionGate is { } gate)
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        foreach (var (predicate, result) in _matchers)
        {
            if (predicate(service, action))
                return result;
        }
        return Default;
    }

    public sealed record RecordedCall(
        Service Service,
        ActionDefinition Action,
        IReadOnlyDictionary<string, string> Inputs);
}
