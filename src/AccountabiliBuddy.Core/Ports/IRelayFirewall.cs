namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// The one inbound Windows Firewall rule that lets other devices reach the relay program (ADR 46). Changing it needs
/// administrator rights, so Windows shows its permission prompt each time.
/// </summary>
public interface IRelayFirewall
{
    /// <returns>True when a rule for this program is in place (reading needs no administrator rights).</returns>
    bool HasRule(string executablePath);

    /// <summary>Makes the rule for this program: allow inbound on Private and Domain networks, and on Public ones when asked. Replaces an older rule.</summary>
    Task<FirewallResult> AllowAsync(string executablePath, bool includePublicNetworks);

    Task<FirewallResult> RemoveAsync(string executablePath);
}
