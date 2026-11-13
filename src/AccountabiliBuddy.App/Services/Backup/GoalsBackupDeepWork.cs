using System.Text.Json.Serialization;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// Deep work's weekly target in a goals backup (ADR 79, GS3a; <see cref="GoalsExport.DeepWork"/>): the minutes in effect, the shape the
/// phone writes and reads (<c>{"weeklyMinutes": 600}</c>, none = <c>{}</c>), plus this PC's optional <c>trackTimeOnly</c>. Never a waiting change.
/// </summary>
/// <param name="WeeklyMinutes">The weekly target in minutes; null = none.</param>
/// <param name="TrackTimeOnly">
/// Whether "Track time only" was on (a PC backup always writes it). Null in a phone-shaped part: a restore then leaves this PC's
/// "Track time only" alone unless the weekly target it restores ends it (a target is stricter).
/// </param>
public sealed record GoalsBackupDeepWork(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? WeeklyMinutes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? TrackTimeOnly = null);
