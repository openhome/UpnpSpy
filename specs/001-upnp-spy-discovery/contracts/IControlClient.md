# Contract: `IControlClient`

**Namespace**: `UpnpSpy.Core.Control`
**Lifetime**: Singleton
**Spec FR**: FR-025, FR-026, FR-027, FR-028, FR-029, FR-030, FR-031
**Protocol source**: UDA 1.0 §3.1

The control client sends a SOAP-over-HTTP action invocation to a service's `controlURL` and returns a discriminated result.

## C# signature

```csharp
public interface IControlClient
{
    Task<InvocationResult> InvokeAsync(
        Service service,
        ActionDefinition action,
        IReadOnlyDictionary<string, string> inputs,   // argName -> user-entered text
        CancellationToken cancellationToken);
}

public abstract record InvocationResult
{
    public sealed record Success(
        IReadOnlyDictionary<string, string> Outputs    // empty when action has no out args (FR-031)
    ) : InvocationResult;

    public sealed record UpnpFault(
        int HttpStatusCode,                            // typically 500
        int UpnpErrorCode,                             // <errorCode>
        string UpnpErrorDescription,                   // <errorDescription>
        string RawFaultXml                             // for diagnostics
    ) : InvocationResult;

    public sealed record TransportError(
        string Message,
        Exception? Underlying
    ) : InvocationResult;
}
```

## Behavioural requirements

- Build the request per UDA 1.0 §3.1.1:
  - Method: `POST`. URL: `service.ControlUrl`.
  - Required headers: `HOST: <host:port>` (from the URL), `CONTENT-LENGTH`, `CONTENT-TYPE: text/xml; charset="utf-8"`, `USER-AGENT: UpnpSpy/1.0 Windows/10`, `SOAPACTION: "<service.ServiceType>#<action.Name>"` (double quotes are mandatory; missing-quote SOAPACTION is the single most common interop bug).
  - Body: a SOAP 1.1 `<s:Envelope>` with `<s:Body>` containing `<u:<action.Name> xmlns:u="<service.ServiceType>">`; one child element per declared input argument, **in SCPD declaration order**, each containing the user-entered text exactly as supplied (no XML escaping beyond `< > & " '`). Actions with zero inputs produce a self-closing action element (FR-031).
- On HTTP 200, parse the response per UDA 1.0 §3.1.2: locate `<u:<action.Name>Response>` and emit one entry per child element into `Outputs`. Actions with zero declared outputs yield an empty dictionary (FR-031).
- On HTTP 500 with a `<s:Fault>` body, parse per UDA 1.0 §3.1.3: extract `<errorCode>` and `<errorDescription>` from the inner `<UPnPError>` element and return `UpnpFault` (FR-029). The full fault body is preserved in `RawFaultXml`.
- On HTTP non-200/non-500, or 500 without a parseable `UPnPError`, return `UpnpFault` with `UpnpErrorCode=0`, `UpnpErrorDescription=reasonPhrase`, and the raw body in `RawFaultXml`.
- On transport failure (`HttpRequestException`, `TaskCanceledException` from timeout, DNS error), return `TransportError` (FR-030). `OperationCanceledException` from the caller's token still propagates.
- The method must not throw under any of the above documented outcomes.

## Failure handling and diagnostics

- Every non-`Success` outcome emits one `DiagnosticEntry`:
  - `UpnpFault` → `Warning`, `Category=Control.Soap`, context: `device.uuid`, `service.id`, `action.name`, `http.status`, `error.code`, `error.text`.
  - `TransportError` → `Warning`, `Category=Control.Transport`, context: `device.uuid`, `service.id`, `action.name`, `url`, `error.text`.

## Test seam

`FakeControlClient` implementations let tests:

- Match by `(service, action)` predicate and return a canned `InvocationResult`.
- Record the inputs dictionary verbatim for assertions on envelope construction. (The actual envelope-building logic lives in `SoapEnvelopeBuilder`, which is unit-tested directly against expected byte-level output.)

## Citations

- UDA 1.0 §3.1.1 Action: Request (HTTP method, headers, SOAP envelope).
- UDA 1.0 §3.1.2 Action: Response (SOAP envelope shape, output arguments).
- UDA 1.0 §3.1.3 Action: Error response (`<UPnPError>` body, error codes 401–501, vendor range 600+).
