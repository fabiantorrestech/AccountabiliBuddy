namespace AccountabiliBuddy.Core.Sync;

/// <summary>An open come-back (ADR 30), as the PC's sealed sync file carries it to the user's other devices.</summary>
public sealed record SyncComeback(Guid Id, string Text, DateTimeOffset CreatedAt);
