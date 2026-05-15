# Contract: `ISsdpTransport`

**Namespace**: `UpnpSpy.Core.Ssdp`
**Lifetime**: Singleton
**Spec FR**: FR-004, FR-006, FR-014, FR-015, FR-021, FR-022, FR-024
**Protocol source**: UDA 1.0 §1.1, §1.2, §1.3

The SSDP transport encapsulates every UDP/multicast operation: enumerating eligible NICs, joining the SSDP multicast group on each, sending M-SEARCH bursts, and exposing every received datagram (whether unicast M-SEARCH response or multicast NOTIFY advertisement) as an async stream.

## C# signature

```csharp
public interface ISsdpTransport : IAsyncDisposable
{
    // Starts the per-NIC sockets and begins listening for NOTIFY advertisements (FR-006).
    // Idempotent: calling Start while already started is a no-op.
    Task StartAsync(CancellationToken cancellationToken);

    // Sends an M-SEARCH burst on every active interface.
    // Used at startup (FR-004) and on rescan (FR-022).
    // Returns when the datagrams have been written; responses arrive via ReceivedMessages.
    Task SendMSearchAsync(
        string searchTarget,            // ST header value, e.g. "ssdp:all"
        TimeSpan maxWait,               // MX header value (1..5 s per UDA 1.0 §1.2.1)
        CancellationToken cancellationToken);

    // The single ordered async stream of every datagram received on every NIC since Start.
    // Backpressure: bounded channel; on overflow the oldest waiting datagram is dropped
    // and one Warning DiagnosticEntry is recorded (FR-039) per overflow event.
    IAsyncEnumerable<ReceivedSsdpDatagram> ReceivedMessages { get; }
}

public sealed record ReceivedSsdpDatagram(
    DateTimeOffset ReceivedUtc,         // from IClock
    string InterfaceName,               // NIC the datagram arrived on
    IPEndPoint RemoteEndpoint,
    ReadOnlyMemory<byte> Payload);      // raw bytes; parser is a separate concern
```

## Behavioural requirements

- On `StartAsync`, query `INetworkInterfaceEnumerator` for the current set of up, non-loopback, multicast-capable IPv4 interfaces (FR-004). For each, create a `UdpClient` bound to that interface's local IPv4 address and join the multicast group `239.255.255.250:1900` on that interface only.
- `SendMSearchAsync` sends to `239.255.255.250:1900` from each per-NIC socket. The HTTP payload is built per UDA 1.0 §1.2.1 (`HOST`, `MAN: "ssdp:discover"`, `MX`, `ST`, `USER-AGENT`). `MAN` value's double quotes are mandatory.
- Receive loops run independently per interface and write each datagram into the shared `Channel<ReceivedSsdpDatagram>` that backs `ReceivedMessages`. The interface a datagram arrived on is preserved (`InterfaceName`).
- `DisposeAsync` cancels the receive loops, leaves the multicast groups, and closes all `UdpClient`s.

## Failure handling

- Per-socket receive errors (`SocketException`) are logged at `Warning` (`Ssdp.Receive`) with the interface name and HRESULT, but do **not** stop the other sockets.
- Sockets that fail to bind at start time emit one `Warning` diagnostic each and are skipped; `StartAsync` itself only throws if **no** interface could be bound (in which case the app surfaces an empty-tree state with a single user-visible message — see spec Edge Cases "Multicast traffic blocked").
- `SendMSearchAsync` swallows per-socket send errors (`Warning` log) — partial sends still produce useful discovery.

## Test seam

`FakeSsdpTransport` exposes:

- A test-controlled `Channel<ReceivedSsdpDatagram>` so unit tests can inject NOTIFY/M-SEARCH responses verbatim.
- A `SentMSearches` list that captures every `SendMSearchAsync` call's parameters for assertion.

## Citations

- UDA 1.0 §1.1 Discovery: Advertisement (alive/byebye semantics).
- UDA 1.0 §1.2 Discovery: Search (M-SEARCH request, response, MX timing).
- UDA 1.0 §1.3 Discovery: Message format (HOST, NT, NTS, MAN, MX, ST, USN, LOCATION, CACHE-CONTROL headers).
