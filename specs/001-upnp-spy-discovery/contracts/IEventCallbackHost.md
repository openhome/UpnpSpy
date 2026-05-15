# Contract: `IEventCallbackHost`

**Namespace**: `UpnpSpy.Core.Eventing`
**Lifetime**: Singleton
**Spec FR**: FR-033
**Protocol source**: UDA 1.0 §4.3

The callback host receives `NOTIFY` HTTP requests sent by subscribed devices and dispatches them to the matching `SubscriptionState`. The production implementation uses `System.Net.HttpListener`; the abstraction keeps unit tests of subscription-popup view-models free of any real HTTP listener.

## C# signature

```csharp
public interface IEventCallbackHost : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    // Register a fresh callback slot. Returns the absolute URL the device should call back on.
    // The URL embeds an opaque token so distinct subscriptions cannot cross-deliver events.
    // 'preferredLocalAddress' is the local IPv4 address that the device is reachable from
    // (per-NIC routing); the URL's host segment uses that address so devices on the LAN
    // can reach the host on the same link they were discovered on.
    EventCallbackRegistration Register(IPAddress preferredLocalAddress);

    // Stream of every parsed NOTIFY received on a given registration's URL.
    // Bounded channel per subscription; on overflow oldest events are dropped and a
    // Warning diagnostic is recorded.
    IAsyncEnumerable<EventNotification> EventsFor(EventCallbackRegistration registration, CancellationToken cancellationToken);

    // Tear down a registration (after UNSUBSCRIBE / popup close).
    ValueTask UnregisterAsync(EventCallbackRegistration registration);
}

public sealed record EventCallbackRegistration(
    Guid Token,
    Uri CallbackUrl);
```

## Behavioural requirements

- `StartAsync` binds an `HttpListener` to a small set of URL prefixes covering each up, non-loopback IPv4 interface address: `http://<localIp>:<port>/upnpspy/`. The port is chosen once at startup (ephemeral, but stable for the run).
- `Register` mints a fresh `Guid` token and returns `http://<preferredLocalAddress>:<port>/upnpspy/<token>/`.
- The accept loop:
  1. Reads each request and verifies the method is `NOTIFY` and the path matches `/upnpspy/<token>/`.
  2. Verifies headers per UDA 1.0 §4.3: `NT: upnp:event`, `NTS: upnp:propchange`, plus `SID` and `SEQ`. Missing/invalid headers produce HTTP 400 + a `Warning` diagnostic (`Category=Eventing.Callback`).
  3. Parses the body as `<e:propertyset>`/`<e:property>` into an `EventNotification` (data-model §11). Malformed XML produces HTTP 400 and a `Warning` diagnostic but is otherwise non-fatal.
  4. On success, writes HTTP 200 (no body) and enqueues the notification into the per-registration channel.
- `DisposeAsync` stops the listener and completes every per-registration channel so subscribed view-models unwind cleanly.

## URL ACL note

`HttpListener` requires a URL reservation under non-elevated accounts. The MSIX install script registers a URL ACL for `http://+:<port>/upnpspy/`. For unpackaged developer builds, the readme documents `netsh http add urlacl url=http://+:<port>/upnpspy/ user=Everyone`. This is operational, not part of the runtime contract — but it is the reason the contract exists at this seam.

## Failure handling

- Bind failure at `StartAsync` → fatal: surfaced to `App` which shows a single user-visible message and disables Subscribe menu items. The rest of the app keeps working.
- Per-request parse/header errors → swallowed (logged) — `IEventCallbackHost` never throws into the accept loop.

## Test seam

`FakeEventCallbackHost`:

- Issues fake `EventCallbackRegistration`s with predictable tokens.
- Lets tests push synthetic `EventNotification` objects through `EventsFor`.

## Citations

- UDA 1.0 §4.3 Event messages (HTTP NOTIFY format, `NT`/`NTS`/`SID`/`SEQ` headers, `<e:propertyset>` body).
- UDA 1.0 §4.1.2 Subscription response (the `SID` value the callback host must match against).
