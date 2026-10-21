namespace AccountabiliBuddy.Core.Sync;

/// <summary>A focus session that counts on the device that wrote the file.</summary>
/// <param name="End">Null for the session that was running when the file was written (<see cref="SyncDeviceCard.WrittenAt"/>).</param>
/// <param name="Profile">The profile's name; null for a session added by hand.</param>
/// <param name="Added">Added by hand in History (ADR 20); its note never travels.</param>
public sealed record SyncSession(DateTimeOffset Start, DateTimeOffset? End, string? Profile, bool Added);
