namespace UpnpSpy.Core.Platform;

public interface INetworkInterfaceEnumerator
{
    IReadOnlyList<EligibleInterface> EnumerateEligible();
}
