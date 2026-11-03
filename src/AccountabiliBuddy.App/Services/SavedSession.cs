using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>The session as last saved to disk, and when the app was last known to be running.</summary>
/// <param name="BootId">ADR 60: the Windows boot it was saved in, to tell a restart from the app being closed; null when unknown.</param>
/// <param name="WindowsEnding">ADR 60: Windows had said it was shutting down, so the PC didn't just lose power.</param>
public sealed record SavedSession(SessionCheckpoint Checkpoint, DateTimeOffset LastAliveAt, string? BootId = null, bool WindowsEnding = false);
