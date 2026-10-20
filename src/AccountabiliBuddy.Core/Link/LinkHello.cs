namespace AccountabiliBuddy.Core.Link;

/// <summary>A paired device signs in.</summary>
public sealed record LinkHello(Guid DeviceId, string Token, string DeviceName, int ProtocolVersion);
