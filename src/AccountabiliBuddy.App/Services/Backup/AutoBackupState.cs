namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>What auto-backup last wrote per kind (document "backup.auto"): the content's hash and when.</summary>
public sealed record AutoBackupState(IReadOnlyDictionary<string, string>? Hashes = null, IReadOnlyDictionary<string, DateTimeOffset>? WrittenAt = null)
{
    public static AutoBackupState Empty { get; } = new();
}
