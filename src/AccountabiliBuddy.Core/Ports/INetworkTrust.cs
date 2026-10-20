namespace AccountabiliBuddy.Core.Ports;

/// <summary>Whether the networks this computer is on are ones the system treats as private (home, work), where the link may listen (ADR 14).</summary>
public interface INetworkTrust
{
    /// <returns>True when every connected Wi-Fi or Ethernet network is marked private; false when one is public, or there is none.</returns>
    bool IsOnPrivateNetwork();
}
