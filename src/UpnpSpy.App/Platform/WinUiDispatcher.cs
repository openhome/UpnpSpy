using Microsoft.UI.Dispatching;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.App.Platform;

public sealed class WinUiDispatcher : IDispatcher
{
    private readonly DispatcherQueue _queue;

    public WinUiDispatcher(DispatcherQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public Task RunOnUiAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_queue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueued = _queue.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        if (!enqueued)
            tcs.SetException(new InvalidOperationException("Dispatcher queue rejected the work item."));
        return tcs.Task;
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _queue.TryEnqueue(() => action());
    }
}
