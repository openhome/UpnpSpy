using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Net;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.Description;

/// <summary>
/// Default <see cref="IScpdFetcher"/>. Issues HTTP GET against a service's
/// SCPDURL (UDA 1.0 §2.4) and pipes the body through <see cref="ScpdXmlParser"/>.
/// </summary>
public sealed class ScpdFetcher : IScpdFetcher
{
    private readonly HttpClient _http;
    private readonly ScpdXmlParser _parser;
    private readonly IDiagnosticSink _diagnostics;
    private readonly IClock _clock;

    public ScpdFetcher(
        HttpClientFactory httpFactory,
        ScpdXmlParser parser,
        IDiagnosticSink diagnostics,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(httpFactory);
        _http = httpFactory.CreateShared();
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ScpdFetchResult> FetchAsync(Uri scpdUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scpdUrl);

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(scpdUrl, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return TransportError(scpdUrl, ex);
        }
        catch (TaskCanceledException ex)
        {
            return TransportError(scpdUrl, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return HttpError(scpdUrl, (int)response.StatusCode, response.ReasonPhrase ?? string.Empty);

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var document = _parser.Parse(stream);
                if (document is null)
                    return ParseError(scpdUrl, "SCPD XML was malformed or missing required elements.");
                return new ScpdFetchResult.Success(document);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException ex)
            {
                return TransportError(scpdUrl, ex);
            }
        }
    }

    private ScpdFetchResult.HttpError HttpError(Uri url, int statusCode, string reason)
    {
        Emit(DiagnosticSeverity.Warning, "Scpd.Fetch",
            $"HTTP {statusCode} fetching SCPD.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["url"] = url.ToString(),
                ["http.status"] = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["error.text"] = reason,
            }, null);
        return new ScpdFetchResult.HttpError(statusCode, reason);
    }

    private ScpdFetchResult.TransportError TransportError(Uri url, Exception ex)
    {
        Emit(DiagnosticSeverity.Warning, "Scpd.Fetch",
            "Transport failure fetching SCPD.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["url"] = url.ToString(),
                ["error.text"] = ex.Message,
            }, ex);
        return new ScpdFetchResult.TransportError(ex.Message, ex);
    }

    private ScpdFetchResult.ParseError ParseError(Uri url, string message)
    {
        Emit(DiagnosticSeverity.Warning, "Scpd.Parse", message,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["url"] = url.ToString(),
            }, null);
        return new ScpdFetchResult.ParseError(message);
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
