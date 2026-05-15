# Contract: `ISubscriptionClient`

**Namespace**: `UpnpSpy.Core.Eventing`
**Lifetime**: Singleton
**Spec FR**: FR-032, FR-033, FR-034, FR-035, FR-036, FR-038
**Protocol source**: UDA 1.0 §4.1, §4.2

The subscription client wraps the three HTTP verbs UPnP eventing uses to manage a subscription: SUBSCRIBE (new), SUBSCRIBE-with-SID (renew), and UNSUBSCRIBE.

## C# signature

```csharp
public interface ISubscriptionClient
{
    // Initial SUBSCRIBE. Returns the server-assigned SID and granted TIMEOUT on success.
    Task<SubscribeResult> SubscribeAsync(
        Service service,
        Uri callbackUrl,                            // a URL the device can reach back on
        TimeSpan requestedTimeout,                  // sent as "Second-<n>" (default 1800 s)
        CancellationToken cancellationToken);

    // Renewal SUBSCRIBE: SID-only, no CALLBACK, no NT. Returns the newly granted timeout.
    Task<RenewResult> RenewAsync(
        Service service,
        string sid,
        TimeSpan requestedTimeout,
        CancellationToken cancellationToken);

    // UNSUBSCRIBE. Best-effort; returns success/failure for diagnostics.
    Task<UnsubscribeResult> UnsubscribeAsync(
        Service service,
        string sid,
        CancellationToken cancellationToken);
}

public abstract record SubscribeResult
{
    public sealed record Success(string Sid, TimeSpan GrantedTimeout) : SubscribeResult;
    public sealed record HttpError(int StatusCode, string ReasonPhrase) : SubscribeResult;
    public sealed record TransportError(string Message, Exception? Underlying) : SubscribeResult;
}

public abstract record RenewResult
{
    public sealed record Success(TimeSpan GrantedTimeout) : RenewResult;
    public sealed record HttpError(int StatusCode, string ReasonPhrase) : RenewResult;
    public sealed record TransportError(string Message, Exception? Underlying) : RenewResult;
}

public abstract record UnsubscribeResult
{
    public sealed record Success : UnsubscribeResult;
    public sealed record HttpError(int StatusCode, string ReasonPhrase) : UnsubscribeResult;
    public sealed record TransportError(string Message, Exception? Underlying) : UnsubscribeResult;
}
```

## Behavioural requirements

### SUBSCRIBE (initial)

- Method: `SUBSCRIBE`. URL: `service.EventSubUrl`. Per UDA 1.0 §4.1.1.
- Headers:
  - `HOST: <host:port>`
  - `CALLBACK: <callbackUrl>` — **angle brackets are mandatory** around the URL (UDA 1.0 §4.1.1).
  - `NT: upnp:event`
  - `TIMEOUT: Second-<requestedTimeout.TotalSeconds rounded to int>`
- No request body.
- On HTTP 2xx, extract `SID` (case-insensitive) and parse `TIMEOUT: Second-<n>` (where `<n>` may also be the literal `infinite`, mapped to `TimeSpan.MaxValue`). Return `Success`.
- On HTTP 4xx/5xx, return `HttpError`. On `HttpRequestException` etc., return `TransportError`.

### RENEW

- Identical to SUBSCRIBE except: no `CALLBACK`, no `NT`, headers are `HOST`, `SID: <sid>`, `TIMEOUT: Second-<n>`. Per UDA 1.0 §4.1.3.
- Same result mapping.

### UNSUBSCRIBE

- Method: `UNSUBSCRIBE`. URL: `service.EventSubUrl`. Headers: `HOST`, `SID: <sid>`. Per UDA 1.0 §4.1.4.
- No request body. On HTTP 2xx return `Success`; otherwise `HttpError`/`TransportError`.

## Failure handling and diagnostics

- Every non-`Success` outcome records a `DiagnosticEntry`:
  - SUBSCRIBE failure → `Warning`, `Category=Eventing.Subscribe`.
  - Renewal failure → `Warning`, `Category=Eventing.Renew`. The popup view-model also transitions the `SubscriptionState` to `Lapsed` (FR-038).
  - UNSUBSCRIBE failure → `Information` (not Warning — best-effort cleanup; we already lost interest in the subscription), `Category=Eventing.Unsubscribe`.
- The contract never throws for documented failures.

## Test seam

`FakeSubscriptionClient` lets tests:

- Queue per-call results so a sequence of (SUBSCRIBE Success, RENEW Success × N, RENEW HttpError(412), UNSUBSCRIBE Success) can be scripted.
- Capture every request's headers and timing for assertion (especially that renewal never sends a `CALLBACK` header and unsubscribe never sends after a `Lapsed` transition).

## Citations

- UDA 1.0 §4.1.1 Subscription with SUBSCRIBE (initial subscription; CALLBACK angle brackets; NT; TIMEOUT).
- UDA 1.0 §4.1.2 Subscription response (`SID`, `TIMEOUT`).
- UDA 1.0 §4.1.3 Renewing a subscription (SID-only SUBSCRIBE).
- UDA 1.0 §4.1.4 Cancelling a subscription (UNSUBSCRIBE).
