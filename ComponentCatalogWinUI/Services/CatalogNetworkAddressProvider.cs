using System.Net.NetworkInformation;
using System.Net.Sockets;
using ComponentCatalogCore.Transport;

namespace ComponentCatalogWinUI.Services;

public sealed record CatalogNetworkAddress(string Address, string InterfaceName, NetworkInterfaceType InterfaceType)
{
    public string DisplayName => $"{ConnectionType} · {Address} ({InterfaceName})";

    public string ConnectionType => InterfaceType switch
    {
        NetworkInterfaceType.Wireless80211 => "WLAN",
        NetworkInterfaceType.Ethernet => "以太网",
        _ => InterfaceName,
    };
}

public static class CatalogNetworkAddressProvider
{
    public static IReadOnlyList<CatalogNetworkAddress> GetAvailablePrivateIpv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
            .Where(networkInterface => networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => new CatalogNetworkAddress(address.Address.ToString(), networkInterface.Name, networkInterface.NetworkInterfaceType)))
            .Where(address => CatalogApiBindingOptions.IsPrivateIpv4(address.Address))
            .OrderBy(address => GetInterfacePriority(address.InterfaceType))
            .ThenBy(address => address.InterfaceName, StringComparer.Ordinal)
            .ThenBy(address => address.Address, StringComparer.Ordinal)
            .ToArray();
    }

    private static int GetInterfacePriority(NetworkInterfaceType interfaceType)
    {
        return interfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => 0,
            NetworkInterfaceType.Ethernet => 1,
            _ => 2,
        };
    }
}
