using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Xml;
using System.Xml.Linq;
using UpnpSpy.Core.Diagnostics;
using UpnpSpy.Core.Models;
using UpnpSpy.Core.Net;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.Core.Control;

/// <summary>
/// Default <see cref="IControlClient"/>. POSTs a UDA 1.0 §3.1 SOAP envelope to
/// the service's control URL and maps the HTTP/SOAP outcome onto an
/// <see cref="InvocationResult"/>.
/// </summary>
public sealed class ControlClient : IControlClient
{
    private const string SoapEnvelopeNs = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string UserAgent = "UpnpSpy/1.0 Windows/10";

    private readonly HttpClient _http;
    private readonly IDiagnosticSink _diagnostics;
    private readonly IClock _clock;

    public ControlClient(HttpClientFactory httpFactory, IDiagnosticSink diagnostics, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(httpFactory);
        _http = httpFactory.CreateShared();
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<InvocationResult> InvokeAsync(
        Service service,
        ActionDefinition action,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(inputs);

        var envelope = SoapEnvelopeBuilder.Build(service.ServiceType, action, inputs);

        using var request = new HttpRequestMessage(HttpMethod.Post, service.ControlUrl);
        request.Content = new ByteArrayContent(envelope);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "\"utf-8\"" };
        request.Content.Headers.ContentLength = envelope.LongLength;
        request.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{service.ServiceType}#{action.Name}\"");
        request.Headers.TryAddWithoutValidation("USER-AGENT", UserAgent);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return TransportError(service, action, ex);
        }
        catch (TaskCanceledException ex)
        {
            return TransportError(service, action, ex);
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException ex)
            {
                return TransportError(service, action, ex);
            }

            if (response.IsSuccessStatusCode)
                return ParseSuccessOrFault(service, action, response, body);

            var statusCode = (int)response.StatusCode;
            var reason = response.ReasonPhrase ?? response.StatusCode.ToString();

            if (response.StatusCode == HttpStatusCode.InternalServerError)
            {
                var fault = SoapFaultParser.Parse(body, reason);
                return UpnpFault(service, action, statusCode, fault.ErrorCode, fault.ErrorDescription, body);
            }

            return UpnpFault(service, action, statusCode, 0, reason, body);
        }
    }

    private InvocationResult ParseSuccessOrFault(
        Service service,
        ActionDefinition action,
        HttpResponseMessage response,
        string body)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };
            using var reader = XmlReader.Create(new StringReader(body), settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return UpnpFault(service, action, (int)response.StatusCode, 0,
                response.ReasonPhrase ?? "Malformed response body", body);
        }

        var responseElement = doc.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, action.Name + "Response", StringComparison.Ordinal));

        if (responseElement is null)
        {
            var faultElement = doc.Descendants()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Fault", StringComparison.Ordinal));
            if (faultElement is not null)
            {
                var fault = SoapFaultParser.Parse(body, response.ReasonPhrase ?? "SOAP fault");
                return UpnpFault(service, action, (int)response.StatusCode, fault.ErrorCode, fault.ErrorDescription, body);
            }

            return UpnpFault(service, action, (int)response.StatusCode, 0,
                "Response did not contain expected action response element.", body);
        }

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var child in responseElement.Elements())
            outputs[child.Name.LocalName] = child.Value;

        return new InvocationResult.Success(outputs, _clock.UtcNow);
    }

    private InvocationResult.UpnpFault UpnpFault(
        Service service,
        ActionDefinition action,
        int httpStatusCode,
        int upnpErrorCode,
        string upnpErrorDescription,
        string rawFaultXml)
    {
        Emit(DiagnosticSeverity.Warning, "Control.Soap",
            $"SOAP fault HTTP {httpStatusCode}: {upnpErrorDescription}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["device.uuid"] = service.OwningDeviceUuid,
                ["service.id"] = service.ServiceId,
                ["action.name"] = action.Name,
                ["http.status"] = httpStatusCode.ToString(CultureInfo.InvariantCulture),
                ["error.code"] = upnpErrorCode.ToString(CultureInfo.InvariantCulture),
                ["error.text"] = upnpErrorDescription,
            }, null);

        return new InvocationResult.UpnpFault(httpStatusCode, upnpErrorCode, upnpErrorDescription, rawFaultXml, _clock.UtcNow);
    }

    private InvocationResult.TransportError TransportError(Service service, ActionDefinition action, Exception ex)
    {
        Emit(DiagnosticSeverity.Warning, "Control.Transport",
            "Transport failure invoking action.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["device.uuid"] = service.OwningDeviceUuid,
                ["service.id"] = service.ServiceId,
                ["action.name"] = action.Name,
                ["url"] = service.ControlUrl.ToString(),
                ["error.text"] = ex.Message,
            }, ex);
        return new InvocationResult.TransportError(ex.Message, ex, _clock.UtcNow);
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
