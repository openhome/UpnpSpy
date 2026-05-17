namespace UpnpSpy.Core.Ssdp;

public interface ISsdpTransport : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// FR-050: closes the current SSDP socket (if any) and rebinds on
    /// whatever adapter the <c>INetworkAdapterSelector</c> currently points
    /// at. Used by the adapter-switch orchestration in <c>ShellViewModel</c>.
    /// Idempotent if not yet started.
    /// </summary>
    Task RestartAsync(CancellationToken cancellationToken);

    Task SendMSearchAsync(
        string searchTarget,
        TimeSpan maxWait,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ReceivedSsdpDatagram> ReceivedMessages { get; }
}
