using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using AccountabiliBuddy.Core.Ports;
using Windows.Win32.Networking.NetworkListManager;

namespace AccountabiliBuddy.Platform.Windows.Network;

/// <summary>
/// Asks Windows' network list which category each connected network has (Public, Private, Domain), counting only
/// networks on real Wi-Fi or Ethernet adapters, so a virtual switch's "Unidentified network" (Hyper-V, WSL) doesn't
/// count as a public network. Kept for Places (phase 12); unused since the Wi-Fi link was deleted (ADR 45).
/// </summary>
public sealed class WindowsNetworkTrust : INetworkTrust
{
    public bool IsOnPrivateNetwork()
    {
        HashSet<Guid> physicalAdapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                              && adapter.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet
                              && !adapter.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                              && !adapter.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
            .Select(adapter => Guid.TryParse(adapter.Id, out Guid id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        if (physicalAdapters.Count == 0)
        {
            return false;
        }

        try
        {
            var manager = (INetworkListManager)new NetworkListManager();
            IEnumNetworks networks = manager.GetNetworks(NLM_ENUM_NETWORK.NLM_ENUM_NETWORK_CONNECTED);
            bool any = false;
            while (Next(networks) is INetwork network)
            {
                if (!AdaptersOf(network).Any(physicalAdapters.Contains))
                {
                    continue;
                }

                any = true;
                if (network.GetCategory() == NLM_NETWORK_CATEGORY.NLM_NETWORK_CATEGORY_PUBLIC)
                {
                    return false;
                }
            }

            return any;
        }
        catch (COMException)
        {
            // Can't tell: treat as not private, so a caller stays cautious on an unknown network.
            return false;
        }
    }

    public unsafe PublicNetwork? FindPublicNetwork()
    {
        Dictionary<Guid, bool> physicalAdapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                              && adapter.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet
                              && !adapter.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                              && !adapter.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
                              && Guid.TryParse(adapter.Id, out _))
            .ToDictionary(adapter => Guid.Parse(adapter.Id), adapter => adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        try
        {
            var manager = (INetworkListManager)new NetworkListManager();
            IEnumNetworks networks = manager.GetNetworks(NLM_ENUM_NETWORK.NLM_ENUM_NETWORK_CONNECTED);
            while (Next(networks) is INetwork network)
            {
                Guid? adapter = AdaptersOf(network).Where(physicalAdapters.ContainsKey).Select(id => (Guid?)id).FirstOrDefault();
                if (adapter is null || network.GetCategory() != NLM_NETWORK_CATEGORY.NLM_NETWORK_CATEGORY_PUBLIC)
                {
                    continue;
                }

                var name = network.GetName();
                try
                {
                    return new PublicNetwork(name.ToString() ?? "this network", physicalAdapters[adapter.Value]);
                }
                finally
                {
                    Marshal.FreeBSTR(new IntPtr(name.Value));
                }
            }

            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static List<Guid> AdaptersOf(INetwork network)
    {
        var adapters = new List<Guid>();
        IEnumNetworkConnections connections = network.GetNetworkConnections();
        while (Next(connections) is INetworkConnection connection)
        {
            adapters.Add(connection.GetAdapterId());
        }

        return adapters;
    }

    private static unsafe INetwork? Next(IEnumNetworks networks)
    {
        var one = new INetwork[1];
        uint fetched = 0;
        networks.Next(1, one, &fetched);
        return fetched == 1 ? one[0] : null;
    }

    private static unsafe INetworkConnection? Next(IEnumNetworkConnections connections)
    {
        var one = new INetworkConnection[1];
        uint fetched = 0;
        connections.Next(1, one, &fetched);
        return fetched == 1 ? one[0] : null;
    }
}
