using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AccountabiliBuddy.Core.Link;

/// <summary>Which addresses count as the local network (the only ones the link answers), and the PC's own address for the QR code.</summary>
public static class LinkAddresses
{
    /// <summary>Loopback, private IPv4 ranges and link-local addresses; IPv6 unique-local and link-local.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] == 10
                   || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168)
                   || (bytes[0] == 169 && bytes[1] == 254);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
               && (address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal);
    }

    /// <summary>
    /// The PC's private IPv4 address on a working Wi-Fi or Ethernet connection with a gateway (the home network),
    /// skipping virtual adapters; null when there is none.
    /// </summary>
    public static IPAddress? PreferredAddress()
    {
        IEnumerable<NetworkInterface> candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                              && adapter.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet
                              && !adapter.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                              && !adapter.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
            .OrderBy(adapter => adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1);

        foreach (NetworkInterface adapter in candidates)
        {
            IPInterfaceProperties properties = adapter.GetIPProperties();
            if (properties.GatewayAddresses.All(gateway => gateway.Address.Equals(IPAddress.Any)))
            {
                continue;
            }

            IPAddress? address = properties.UnicastAddresses
                .Select(unicast => unicast.Address)
                .FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork
                                             && IsPrivate(candidate) && !IPAddress.IsLoopback(candidate));
            if (address is not null)
            {
                return address;
            }
        }

        return null;
    }
}
