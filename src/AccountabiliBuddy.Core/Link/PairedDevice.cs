namespace AccountabiliBuddy.Core.Link;

/// <summary>A device paired with this PC. Only a hash of its token is kept, so the token can't be read back.</summary>
/// <param name="Kind">"phone", "tablet" or "watch" (ADR 21: gets the session and today's focus only).</param>
public sealed record PairedDevice(Guid Id, string Name, string Kind, string TokenHash, DateTimeOffset PairedAt, DateTimeOffset? LastSeenAt);
