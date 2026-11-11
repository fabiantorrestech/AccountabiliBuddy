namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// One copy of the account's Tasks settings to choose from (<see cref="TaskAccountSettingsService.AccountCopy"/>,
/// <see cref="TaskAccountSettingsService.FromBackup"/>, <see cref="TaskAccountSettingsService.Fresh"/>), applied with
/// <see cref="TaskAccountSettingsService.ApplyAsync"/>.
/// </summary>
/// <param name="Origin">Where the copy comes from.</param>
/// <param name="DefaultViewId">The view the Shelf opens with; null for the built-in Default.</param>
/// <param name="PickedCalendarId">The calendar tasks with a due date go to; null when off.</param>
/// <param name="EditedAt">When its newest setting was chosen; null when unknown (nothing chosen since the settings were shared, or a fresh start).</param>
/// <param name="BackupMadeAt">When the backup was made; null for the account's copy and a fresh start.</param>
public sealed record TaskAccountSettingsChoice(
    TaskAccountSettingsOrigin Origin,
    Guid? DefaultViewId,
    string? PickedCalendarId,
    DateTimeOffset? EditedAt,
    DateTimeOffset? BackupMadeAt);
