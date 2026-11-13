using System.Text.Json.Serialization;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// Deep work's weekly target in a goals backup (ADR 79, GS3a; <see cref="GoalsExport.DeepWork"/>): the minutes in effect, the shape the
/// phone writes and reads (<c>{"weeklyMinutes": 600}</c>, none = <c>{}</c>). Never a waiting change.
/// </summary>
/// <param name="WeeklyMinutes">The weekly target in minutes; null = none.</param>
public sealed record GoalsBackupDeepWork([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? WeeklyMinutes);
