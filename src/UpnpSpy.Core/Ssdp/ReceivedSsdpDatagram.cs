using System.Net;

namespace UpnpSpy.Core.Ssdp;

public sealed record ReceivedSsdpDatagram(
    DateTimeOffset ReceivedUtc,
    string InterfaceName,
    IPEndPoint RemoteEndpoint,
    ReadOnlyMemory<byte> Payload);
