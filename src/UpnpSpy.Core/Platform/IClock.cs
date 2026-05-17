namespace UpnpSpy.Core.Platform;

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Waits asynchronously for the given duration. Production implementations
    /// delegate to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>; tests
    /// supply a fake that gates completion manually so timing-sensitive logic
    /// (subscription renewal, MX windows) can be driven deterministically.
    /// </summary>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
