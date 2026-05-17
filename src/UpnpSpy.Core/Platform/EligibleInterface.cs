using System.Net;

namespace UpnpSpy.Core.Platform;

/// <summary>
/// A network interface that is eligible for SSDP multicast: operational, non-loopback,
/// multicast-capable, with an IPv4 address.
/// </summary>
public sealed record EligibleInterface(string Name, IPAddress Ipv4Address);
