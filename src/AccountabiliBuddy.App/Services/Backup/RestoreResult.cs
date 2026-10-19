namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>What a restore did.</summary>
/// <param name="NeedsRestart">Settings or calendars were restored; the app restarts to load them.</param>
/// <param name="SafetyBackupPath">The automatic backup of the previous state (overwrite only), for undoing it.</param>
/// <param name="Summary">One line per restored part.</param>
public sealed record RestoreResult(bool NeedsRestart, string? SafetyBackupPath, IReadOnlyList<string> Summary);
