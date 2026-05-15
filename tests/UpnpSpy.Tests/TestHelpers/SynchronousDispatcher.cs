using UpnpSpy.Core.Platform;

namespace UpnpSpy.Tests.TestHelpers;

/// <summary>IDispatcher fake that runs actions inline on the calling thread.</summary>
internal sealed class SynchronousDispatcher : IDispatcher
{
    public Task RunOnUiAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public void Post(Action action) => action();
}
