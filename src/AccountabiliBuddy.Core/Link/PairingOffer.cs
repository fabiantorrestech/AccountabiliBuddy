namespace AccountabiliBuddy.Core.Link;

/// <summary>What the Setup tab shows while pairing: the link in the QR code, the code itself and when it expires.</summary>
public sealed record PairingOffer(string Link, string Code, string Address, int Port, string Fingerprint, DateTimeOffset ExpiresAt);
