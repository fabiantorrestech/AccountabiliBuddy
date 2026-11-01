namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>The outcome of writing a removed device's backup (<see cref="DeviceBackupService"/>, ADR 55).</summary>
/// <param name="Done">True when the backup was written.</param>
/// <param name="Where">Where it went, for the user: the Drive path or the file path; null when not written.</param>
/// <param name="Problem">In plain words why not, when <paramref name="Done"/> is false.</param>
/// <param name="DriveRefused">True when Google Drive refused the upload (a full Drive answers 403): the local file is offered then.</param>
public sealed record BackupOutcome(bool Done, string? Where, string? Problem, bool DriveRefused = false)
{
    public static BackupOutcome Saved(string where) => new(true, where, null);

    public static BackupOutcome Failed(string problem, bool driveRefused = false) => new(false, null, problem, driveRefused);
}
