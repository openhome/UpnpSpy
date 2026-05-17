# Contract: `IDeviceDescriptionFetcher` and `IScpdFetcher`

**Namespace**: `UpnpSpy.Core.Description`
**Lifetime**: Singleton
**Spec FR**: FR-011, FR-012, FR-013
**Protocol source**: UDA 1.0 §2.1, §2.2, §2.4

Two sibling contracts that fetch UPnP description documents over HTTP and parse them into domain types. They are kept separate because the failure modes are observed at different points in the UI (device-node expansion vs. service-node expansion) and because separating them keeps both implementations under the 200-LOC target.

## C# signatures

```csharp
public interface IDeviceDescriptionFetcher
{
    // Fetches the device description XML referenced by 'locationUrl' and parses
    // the root <device> element into a DeviceDescription record. <deviceList>
    // children are walked recursively and every <service> at any depth is
    // emitted as a ServiceDescriptor with its ContainingDeviceUdn populated;
    // embedded devices themselves are not surfaced (see research §20).
    Task<DeviceDescriptionFetchResult> FetchAsync(Uri locationUrl, CancellationToken cancellationToken);
}

public sealed record DeviceDescription(
    string Uuid,                              // from root <UDN>: "uuid:<UUID>"
    string? FriendlyName,                     // root device's <friendlyName>; null/empty if not provided
    IReadOnlyList<ServiceDescriptor> Services); // flattened union of root + embedded children, resolved against locationUrl base

public sealed record ServiceDescriptor(
    string ContainingDeviceUdn,               // UDN of the immediate parent <device> (root or embedded)
    string? ContainingDeviceFriendlyName,     // <friendlyName> of the parent <device>, null if missing
    string ServiceId,
    string ServiceType,
    Uri ScpdUrl,
    Uri ControlUrl,
    Uri EventSubUrl);

public abstract record DeviceDescriptionFetchResult
{
    public sealed record Success(DeviceDescription Description) : DeviceDescriptionFetchResult;
    public sealed record HttpError(int StatusCode, string ReasonPhrase) : DeviceDescriptionFetchResult;
    public sealed record TransportError(string Message, Exception? Underlying) : DeviceDescriptionFetchResult;
    public sealed record ParseError(string Message) : DeviceDescriptionFetchResult;
}

public interface IScpdFetcher
{
    Task<ScpdFetchResult> FetchAsync(Uri scpdUrl, CancellationToken cancellationToken);
}

public sealed record ScpdDocument(
    IReadOnlyList<ActionDefinition> Actions,
    IReadOnlyList<StateVariableDefinition> StateVariables);

public abstract record ScpdFetchResult
{
    public sealed record Success(ScpdDocument Document) : ScpdFetchResult;
    public sealed record HttpError(int StatusCode, string ReasonPhrase) : ScpdFetchResult;
    public sealed record TransportError(string Message, Exception? Underlying) : ScpdFetchResult;
    public sealed record ParseError(string Message) : ScpdFetchResult;
}
```

## Behavioural requirements

- Use the shared singleton `HttpClient` with a 5 s timeout. Issue `GET` against the URL (UDA 1.0 §2.4).
- Resolve every relative URL inside the device description (`SCPDURL`, `controlURL`, `eventSubURL`) against the response's effective `Content-Location` or, failing that, against the request URL — never against the raw `<URLBase>` element, which UDA 1.0 §2.1 deprecates as of erratum-2008. URLs declared by embedded child services resolve against this same root-description base, **not** against the containing `<device>` element (research §20).
- Strip the leading `uuid:` from `<UDN>` before producing `Uuid` (root device) — but preserve `uuid:<UUID>` verbatim in `ContainingDeviceUdn` because identity within the root's services is compared as written.
- Walk `<deviceList>` recursively. For each `<service>` encountered, emit a `ServiceDescriptor` whose `ContainingDeviceUdn` / `ContainingDeviceFriendlyName` come from its immediate parent `<device>`. If two `<service>` elements within the same `<device>` share `<serviceId>`, drop the second and emit one `Warning` `Description.Parse` diagnostic (UDA 1.0 §2.1 implicitly forbids the collision).
- Use a non-namespace-validating XML reader with DTD/XInclude disabled (defence in depth — devices ship arbitrary XML).
- All four `*Result` outcomes are normal flow: callers branch on them. Implementations must not throw for HTTP non-success, transport failure, or malformed XML.
- `OperationCanceledException` propagates as normal on token cancellation.

## Failure handling

- Each non-success result records a `Warning`-level `DiagnosticEntry` with `Category=Description.Fetch|Description.Parse|Scpd.Fetch|Scpd.Parse` and structured context (`url`, `http.status`, `error.text`) before returning.
- View-models translate non-success outcomes into the inline tree message specified by FR-013.

## Test seam

`FakeDeviceDescriptionFetcher` and `FakeScpdFetcher` let tests:

- Pre-program a result per URL (success with a canned document, or any of the three failure shapes).
- Assert call counts (verifying laziness: a device is fetched exactly once on first expansion).

## Citations

- UDA 1.0 §2.1 Device description (root `<device>` element, `<UDN>`, `<friendlyName>`, `<serviceList>`).
- UDA 1.0 §2.2 Service description (SCPD: actions, arguments, state variables).
- UDA 1.0 §2.4 Retrieving a description via HTTP/1.1 GET.
