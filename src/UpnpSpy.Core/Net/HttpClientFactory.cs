namespace UpnpSpy.Core.Net;

/// <summary>
/// Owns a single process-wide <see cref="HttpClient"/> with the timeout, connection
/// pooling, and redirect behaviour Spec §plan calls for. Singleton lifetime — do not
/// dispose the returned client; dispose the factory when the app shuts down instead.
/// </summary>
public sealed class HttpClientFactory : IDisposable
{
    private readonly SocketsHttpHandler _handler;
    private readonly HttpClient _shared;

    public HttpClientFactory()
    {
        _handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AllowAutoRedirect = true,
            MaxConnectionsPerServer = 8,
        };
        _shared = new HttpClient(_handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    public HttpClient CreateShared() => _shared;

    public void Dispose()
    {
        _shared.Dispose();
        _handler.Dispose();
    }
}
