namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Whether the networks this computer is on are ones the system treats as private (home, work). Kept for Places (phase 12);
/// unused since the Wi-Fi link was deleted (ADR 45).
/// </summary>
public interface INetworkTrust
{
    /// <returns>True when every connected Wi-Fi or Ethernet network is marked private; false when one is public, or there is none.</returns>
    bool IsOnPrivateNetwork();

    /// <returns>A connected Wi-Fi or Ethernet network marked public (the first found), or null.</returns>
    PublicNetwork? FindPublicNetwork();
}
