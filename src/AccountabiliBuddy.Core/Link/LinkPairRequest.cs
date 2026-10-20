namespace AccountabiliBuddy.Core.Link;

/// <summary>A new device asks to pair with the one-time code from the PC's QR code.</summary>
/// <param name="DeviceKind">"phone" or "tablet".</param>
public sealed record LinkPairRequest(string Code, string DeviceName, string DeviceKind);
