using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Net;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.Description;

/// <summary>
/// Default <see cref="IDeviceDescriptionFetcher"/>. Issues an HTTP GET against
/// the device's LOCATION URL (UDA 1.0 §2.4), pipes the body through
/// <see cref="DeviceDescriptionXmlParser"/>, and resolves every relative URL
/// inside against the response's <c>Content-Location</c> (when present) or the
/// request URL otherwise.
/// </summary>
public sealed class DeviceDescriptionFetcher : IDeviceDescriptionFetcher
{
    private readonly HttpClient _http;
    private readonly DeviceDescriptionXmlParser _parser;
    private readonly IDiagnosticSink _diagnostics;
    private readonly IClock _clock;

    public DeviceDescriptionFetcher(
        HttpClientFactory httpFactory,
        DeviceDescriptionXmlParser parser,
        IDiagnosticSink diagnostics,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(httpFactory);
        _http = httpFactory.CreateShared();
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<DeviceDescriptionFetchResult> FetchAsync(Uri locationUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locationUrl);

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(locationUrl, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return TransportError(locationUrl, ex);
        }
        catch (TaskCanceledException ex)
        {
            return TransportError(locationUrl, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return HttpError(locationUrl, (int)response.StatusCode, response.ReasonPhrase ?? string.Empty);

            var effectiveBase = response.Content.Headers.ContentLocation is { } cl
                ? new Uri(locationUrl, cl)
                : (response.RequestMessage?.RequestUri ?? locationUrl);

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var description = _parser.Parse(stream, effectiveBase);
                if (description is null)
                    return ParseError(locationUrl, "Device description XML missing required <device>/<UDN> or was malformed.");
                return new DeviceDescriptionFetchResult.Success(description);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException ex)
            {
                return TransportError(locationUrl, ex);
            }
        }
    }

    private DeviceDescriptionFetchResult.HttpError HttpError(Uri url, int statusCode, string reason)
    {
        Emit(DiagnosticSeverity.Warning, "Description.Fetch",
            $"HTTP {statusCode} fetching device description.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["url"] = url.ToString(),
                ["http.status"] = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["error.text"] = reason,
            }, null);
        return new DeviceDescriptionFetchResult.HttpError(statusCode, reason);
    }

    private DeviceDescriptionFetchResult.TransportError TransportError(Uri url, Exception ex)
    {
        Emit(DiagnosticSeverity.Warning, "Description.Fetch",
            "Transport failure fetching device description.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["url"] = url.ToString(),
                ["error.text"] = ex.Message,
            }, ex);
        return new DeviceDescriptionFetchResult.TransportError(ex.Message, ex);
    }

    private DeviceDescriptionFetchResult.ParseError ParseError(Uri url, string message)
    {
        Emit(DiagnosticSeverity.Warning, "Description.Parse", message,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["url"] = url.ToString(),
            }, null);
        return new DeviceDescriptionFetchResult.ParseError(message);
    }

    private void Emit(DiagnosticSeverity severity, string category, string message,
        IReadOnlyDictionary<string, string> context, Exception? exception) =>
        _diagnostics.Record(new DiagnosticEntry(
            Timestamp: _clock.UtcNow,
            Severity: severity,
            Category: category,
            Message: message,
            Context: context,
            Exception: exception?.ToString()));
}
