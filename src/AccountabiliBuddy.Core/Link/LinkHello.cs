namespace AccountabiliBuddy.Core.Link;

/// <summary>A paired device signs in; <paramref name="SettingsVersion"/> is the settings version it already has.</summary>
public sealed record LinkHello(Guid DeviceId, string Token, string DeviceName, int ProtocolVersion, string? SettingsVersion = null);
