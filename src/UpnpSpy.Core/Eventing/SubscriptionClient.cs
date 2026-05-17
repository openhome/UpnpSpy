using System.Globalization;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Net;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.Eventing;

/// <summary>
/// Default <see cref="ISubscriptionClient"/>. Issues the GENA HTTP verbs against
/// the service's <c>eventSubURL</c> per UDA 1.0 §4.1, parses <c>SID</c> and
/// <c>TIMEOUT</c> from the response, and records a <see cref="DiagnosticEntry"/>
/// for every non-success outcome.
/// </summary>
public sealed class SubscriptionClient : ISubscriptionClient
{
    private const string UserAgent = "UpnpSpy/1.0 Windows/10";

    private static readonly HttpMethod Subscribe = new("SUBSCRIBE");
    private static readonly HttpMethod Unsubscribe = new("UNSUBSCRIBE");

    private readonly HttpClient _http;
    private readonly IDiagnosticSink _diagnostics;
    private readonly IClock _clock;

    public SubscriptionClient(HttpClientFactory httpFactory, IDiagnosticSink diagnostics, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(httpFactory);
        _http = httpFactory.CreateShared();
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<SubscribeResult> SubscribeAsync(
        Service service,
        Uri callbackUrl,
        TimeSpan requestedTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(callbackUrl);

        using var request = new HttpRequestMessage(Subscribe, service.EventSubUrl);
        request.Headers.TryAddWithoutValidation("CALLBACK", $"<{callbackUrl}>");
        request.Headers.TryAddWithoutValidation("NT", "upnp:event");
        request.Headers.TryAddWithoutValidation("TIMEOUT", FormatTimeout(requestedTimeout));
        request.Headers.TryAddWithoutValidation("USER-AGENT", UserAgent);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return SubscribeTransport(service, ex);
        }
        catch (TaskCanceledException ex)
        {
            return SubscribeTransport(service, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var reason = response.ReasonPhrase ?? response.StatusCode.ToString();
                Emit(DiagnosticSeverity.Warning, "Eventing.Subscribe",
                    $"SUBSCRIBE failed HTTP {status} {reason}",
                    SubscribeContext(service, status), null);
                return new SubscribeResult.HttpError(status, reason);
            }

            var sid = SingleHeader(response, "SID");
            var timeoutHeader = SingleHeader(response, "TIMEOUT");

            if (string.IsNullOrEmpty(sid) || !TryParseTimeout(timeoutHeader, out var granted))
            {
                var status = (int)response.StatusCode;
                var msg = string.IsNullOrEmpty(sid)
                    ? "SUBSCRIBE response missing SID header."
                    : "SUBSCRIBE response missing/invalid TIMEOUT header.";
                Emit(DiagnosticSeverity.Warning, "Eventing.Subscribe", msg,
                    SubscribeContext(service, status), null);
                return new SubscribeResult.HttpError(status, msg);
            }

            return new SubscribeResult.Success(sid!, granted);
        }
    }

    public async Task<RenewResult> RenewAsync(
        Service service,
        string sid,
        TimeSpan requestedTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrEmpty(sid);

        using var request = new HttpRequestMessage(Subscribe, service.EventSubUrl);
        request.Headers.TryAddWithoutValidation("SID", sid);
        request.Headers.TryAddWithoutValidation("TIMEOUT", FormatTimeout(requestedTimeout));
        request.Headers.TryAddWithoutValidation("USER-AGENT", UserAgent);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return RenewTransport(service, sid, ex);
        }
        catch (TaskCanceledException ex)
        {
            return RenewTransport(service, sid, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var reason = response.ReasonPhrase ?? response.StatusCode.ToString();
                Emit(DiagnosticSeverity.Warning, "Eventing.Renew",
                    $"Renewal failed HTTP {status} {reason}",
                    RenewContext(service, sid, status), null);
                return new RenewResult.HttpError(status, reason);
            }

            var timeoutHeader = SingleHeader(response, "TIMEOUT");
            if (!TryParseTimeout(timeoutHeader, out var granted))
            {
                var status = (int)response.StatusCode;
                const string msg = "Renewal response missing/invalid TIMEOUT header.";
                Emit(DiagnosticSeverity.Warning, "Eventing.Renew", msg,
                    RenewContext(service, sid, status), null);
                return new RenewResult.HttpError(status, msg);
            }

            return new RenewResult.Success(granted);
        }
    }

    public async Task<UnsubscribeResult> UnsubscribeAsync(
        Service service,
        string sid,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrEmpty(sid);

        using var request = new HttpRequestMessage(Unsubscribe, service.EventSubUrl);
        request.Headers.TryAddWithoutValidation("SID", sid);
        request.Headers.TryAddWithoutValidation("USER-AGENT", UserAgent);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            Emit(DiagnosticSeverity.Information, "Eventing.Unsubscribe",
                "UNSUBSCRIBE transport failure.",
                UnsubscribeContext(service, sid, ex.Message), ex);
            return new UnsubscribeResult.TransportError(ex.Message, ex);
        }
        catch (TaskCanceledException ex)
        {
            Emit(DiagnosticSeverity.Information, "Eventing.Unsubscribe",
                "UNSUBSCRIBE transport failure.",
                UnsubscribeContext(service, sid, ex.Message), ex);
            return new UnsubscribeResult.TransportError(ex.Message, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var reason = response.ReasonPhrase ?? response.StatusCode.ToString();
                Emit(DiagnosticSeverity.Information, "Eventing.Unsubscribe",
                    $"UNSUBSCRIBE returned HTTP {status} {reason}",
                    UnsubscribeContext(service, sid, reason), null);
                return new UnsubscribeResult.HttpError(status, reason);
            }
            return new UnsubscribeResult.Success();
        }
    }

    private SubscribeResult.TransportError SubscribeTransport(Service service, Exception ex)
    {
        Emit(DiagnosticSeverity.Warning, "Eventing.Subscribe",
            "SUBSCRIBE transport failure.",
            SubscribeContext(service, 0, ex.Message), ex);
        return new SubscribeResult.TransportError(ex.Message, ex);
    }

    private RenewResult.TransportError RenewTransport(Service service, string sid, Exception ex)
    {
        Emit(DiagnosticSeverity.Warning, "Eventing.Renew",
            "Renewal transport failure.",
            RenewContext(service, sid, 0, ex.Message), ex);
        return new RenewResult.TransportError(ex.Message, ex);
    }

    private static string FormatTimeout(TimeSpan requested)
    {
        if (requested == TimeSpan.MaxValue) return "infinite";
        var seconds = Math.Max(1, (long)Math.Round(requested.TotalSeconds, MidpointRounding.AwayFromZero));
        return "Second-" + seconds.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryParseTimeout(string? header, out TimeSpan timeout)
    {
        timeout = default;
        if (string.IsNullOrWhiteSpace(header)) return false;

        var trimmed = header.Trim();
        if (trimmed.StartsWith("Second-", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed["Second-".Length..].Trim();
            if (string.Equals(rest, "infinite", StringComparison.OrdinalIgnoreCase))
            {
                timeout = TimeSpan.MaxValue;
                return true;
            }
            if (long.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                timeout = TimeSpan.FromSeconds(seconds);
                return true;
            }
        }
        else if (string.Equals(trimmed, "infinite", StringComparison.OrdinalIgnoreCase))
        {
            timeout = TimeSpan.MaxValue;
            return true;
        }

        return false;
    }

    private static string? SingleHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
            return values.FirstOrDefault();
        if (response.Content.Headers.TryGetValues(name, out var contentValues))
            return contentValues.FirstOrDefault();
        return null;
    }

    private static Dictionary<string, string> SubscribeContext(Service service, int status, string? error = null)
    {
        var ctx = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["device.uuid"] = service.OwningDeviceUuid,
            ["service.id"] = service.ServiceId,
            ["url"] = service.EventSubUrl.ToString(),
            ["http.status"] = status.ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrEmpty(error))
            ctx["error.text"] = error;
        return ctx;
    }

    private static Dictionary<string, string> RenewContext(Service service, string sid, int status, string? error = null)
    {
        var ctx = SubscribeContext(service, status, error);
        ctx["subscription.sid"] = sid;
        return ctx;
    }

    private static Dictionary<string, string> UnsubscribeContext(Service service, string sid, string? error = null)
    {
        var ctx = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["device.uuid"] = service.OwningDeviceUuid,
            ["service.id"] = service.ServiceId,
            ["url"] = service.EventSubUrl.ToString(),
            ["subscription.sid"] = sid,
        };
        if (!string.IsNullOrEmpty(error))
            ctx["error.text"] = error;
        return ctx;
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
