namespace UpnpSpy.Core.Platform;

/// <summary>
/// Marshals work onto the UI thread. Implemented in App by an adapter over the
/// main window's DispatcherQueue; faked in tests by a synchronous executor.
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread and completes when it returns.
    /// </summary>
    Task RunOnUiAsync(Action action);

    /// <summary>
    /// Queues <paramref name="action"/> for the UI thread without waiting.
    /// </summary>
    void Post(Action action);
}
