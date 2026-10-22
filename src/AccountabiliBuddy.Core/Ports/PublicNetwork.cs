namespace AccountabiliBuddy.Core.Ports;

/// <summary>A connected network the system treats as public, where the link doesn't listen (ADR 14).</summary>
/// <param name="Name">As the system shows it, e.g. the Wi-Fi's name.</param>
/// <param name="IsWireless">Wi-Fi (else Ethernet), to open the right settings page.</param>
public sealed record PublicNetwork(string Name, bool IsWireless);
