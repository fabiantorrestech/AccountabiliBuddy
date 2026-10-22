namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A focus session another device counted (ADR 22), as this PC shows it in History and on the day bar.</summary>
/// <param name="End">When it ended; for the one running when its file was written, that file's time.</param>
/// <param name="Profile">Its profile's name; null for a session added by hand.</param>
/// <param name="Added">Added by hand in that device's History (ADR 20).</param>
public sealed record ReceivedSession(Guid DeviceId, string DeviceName, DateTimeOffset Start, DateTimeOffset End, string? Profile, bool Added);
