using System.Net.NetworkInformation;
using System.Net.Sockets;
using UpnpSpy.Core.Platform;

namespace UpnpSpy.App.Platform;

public sealed class NetworkInterfaceEnumerator : INetworkInterfaceEnumerator
{
    public IReadOnlyList<EligibleInterface> EnumerateEligible()
    {
        var results = new List<EligibleInterface>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.IsReceiveOnly) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (!nic.SupportsMulticast) continue;
            if (!nic.Supports(NetworkInterfaceComponent.IPv4)) continue;

            var props = nic.GetIPProperties();
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                results.Add(new EligibleInterface(nic.Name, ua.Address));
                break;
            }
        }
        return results;
    }
}
