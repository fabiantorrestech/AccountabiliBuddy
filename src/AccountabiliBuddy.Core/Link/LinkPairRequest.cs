namespace AccountabiliBuddy.Core.Link;

/// <summary>A new device asks to pair with the one-time code from the PC's QR code.</summary>
/// <param name="DeviceKind">"phone", "tablet" or "watch" (its code comes through the phone, ADR 21).</param>
public sealed record LinkPairRequest(string Code, string DeviceName, string DeviceKind);
