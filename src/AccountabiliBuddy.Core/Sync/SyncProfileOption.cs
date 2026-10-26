namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// One of a PC's profiles as its file always lists them (ADR 42, <see cref="SyncFile.ProfileOptions"/>), for the
/// phone's start picker: read-only, the id and the name only.
/// </summary>
public sealed record SyncProfileOption(Guid Id, string Name);
