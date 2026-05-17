using UpnpSpy.Core.Platform;

namespace UpnpSpy.Tests.TestHelpers;

internal sealed class FakeClock : IClock
{
    private readonly object _gate = new();
    private readonly List<PendingDelay> _pending = new();

    public DateTimeOffset UtcNow { get; set; } = new(2026, 5, 12, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Snapshot of every delay currently outstanding. Useful for asserting that
    /// the scheduler under test parked for the expected duration.
    /// </summary>
    public IReadOnlyList<TimeSpan> PendingDelays
    {
        get
        {
            lock (_gate) return _pending.Select(p => p.Delay).ToArray();
        }
    }

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        var pending = new PendingDelay(delay, tcs, registration);
        lock (_gate) _pending.Add(pending);
        return tcs.Task;
    }

    /// <summary>
    /// Completes every outstanding delay successfully. Tests drive the scheduler
    /// forward by calling this after asserting the delay was queued.
    /// </summary>
    public void CompleteAllDelays()
    {
        PendingDelay[] snapshot;
        lock (_gate)
        {
            snapshot = _pending.ToArray();
            _pending.Clear();
        }
        foreach (var p in snapshot)
        {
            p.Registration.Dispose();
            p.Source.TrySetResult();
        }
    }

    private sealed record PendingDelay(TimeSpan Delay, TaskCompletionSource Source, CancellationTokenRegistration Registration);
}
