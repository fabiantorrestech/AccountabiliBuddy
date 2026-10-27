namespace AccountabiliBuddy.Core.Ports;

/// <summary>A connected network the system treats as public. Kept for Places (phase 12); unused since the Wi-Fi link was deleted (ADR 45).</summary>
/// <param name="Name">As the system shows it, e.g. the Wi-Fi's name.</param>
/// <param name="IsWireless">Wi-Fi (else Ethernet), to open the right settings page.</param>
public sealed record PublicNetwork(string Name, bool IsWireless);
