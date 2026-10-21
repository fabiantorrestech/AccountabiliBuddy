namespace AccountabiliBuddy.Core.Link;

/// <summary>A focus session that counts, cut to the day.</summary>
/// <param name="End">Null for the session running now.</param>
/// <param name="Profile">The profile's name; null for a session added by hand.</param>
public sealed record LinkFocusSession(DateTimeOffset Start, DateTimeOffset? End, string? Profile, bool Added);
