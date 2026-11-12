using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One goal (brief §5.1). Carries the shared-item stamps of ADR 48/72 (stable id, position, edit time, tombstone) so syncing
/// it later (ADR 79, Proposed) is a merge, not a migration; nothing syncs it now. Its day values and statuses are computed
/// (<see cref="GoalCounting"/>), never stored.
/// </summary>
/// <param name="AreaId">The <see cref="GoalArea"/> it sits in.</param>
/// <param name="Name">"Spanish". The user's private text: sealed at rest with the device's key, never logged; a name: in a backup it travels readable like a task title. The target is a separate field everywhere.</param>
/// <param name="Icon">A Material icon name (ADR 32), shown everywhere the goal appears.</param>
/// <param name="Color">"#RRGGBB", the heatmap's ramp and the goal's chips.</param>
/// <param name="Sources">What counts toward it; a minute counts once per goal however many rules match it. A rule of a newer app (a type this version doesn't know) is kept as written beside them (<see cref="GoalNewerJson.NewerOf{T}"/>), never counted (ADR 79).</param>
/// <param name="CreatedAt">For the goal's age ("6 months going", <see cref="GoalAge"/>) and where its first period starts.</param>
/// <param name="Position">Its place in its area: lower first.</param>
/// <param name="EditedAt">Its newest change: the item stamp (newest edit wins).</param>
/// <param name="BuiltIn">True for Deep work only: it can't be paused or deleted, only switched to "Track time only".</param>
/// <param name="SecondTarget">
/// Deep work's weekly target next to its daily one (<paramref name="Target"/> is a view over the PC's "goal" document, which
/// stays the source of truth, brief §5.3); null for every other goal and for a Deep work without a weekly target.
/// </param>
/// <param name="Pauses">The stretches it was paused, oldest first; one still going has no end. Null = never paused.</param>
/// <param name="ShowOnToday">Whether Today's goals card shows it.</param>
/// <param name="ExtraTiles">Streak and Total tiles on its detail panel; off by default.</param>
/// <param name="Notes">Why it matters, with links. The user's private text: sealed at rest with the device's key, never logged; in a backup only inside a password file.</param>
/// <param name="DeletedAt">Set on a tombstone (kept 30 days, no text).</param>
public sealed record Goal(
    Guid Id,
    Guid AreaId,
    string Name,
    string Icon,
    string Color,
    GoalKind Kind,
    GoalTarget Target,
    [property: JsonConverter(typeof(GoalNewerListConverter<GoalSourceRule>))] IReadOnlyList<GoalSourceRule> Sources,
    DateTimeOffset CreatedAt,
    double Position,
    DateTimeOffset EditedAt,
    bool BuiltIn = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalTarget? SecondTarget = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<GoalPauseStretch>? Pauses = null,
    bool ShowOnToday = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalExtraTiles? ExtraTiles = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null)
{
    /// <summary>Fields written by a newer app, kept and written back unchanged (ADR 79 forward compatibility); null when there are none.</summary>
    [JsonExtensionData]
    public GoalUnknownFields? Extra { get; init; }

    /// <summary>The built-in Deep work goal's id: the same on every device, so its record merges as one item once goals sync.</summary>
    public static Guid DeepWorkId { get; } = Guid.Parse("d33b0a11-6f0c-4e2a-9d51-0000000000d1");

    [JsonIgnore]
    public bool IsLive => DeletedAt is null;

    /// <summary>
    /// Whether this is the built-in Deep work goal: flagged built in, or carrying its fixed id (a record that lost the flag is
    /// still Deep work). It counts all focus and nothing else (plan §3b 6) and its lowering follows the guarded rule.
    /// </summary>
    [JsonIgnore]
    public bool IsDeepWork => BuiltIn || Id == DeepWorkId;

    /// <summary>Whether the goal is paused on <paramref name="day"/>.</summary>
    public bool IsPausedOn(DateOnly day)
    {
        return (Pauses ?? []).Any(stretch => stretch.Covers(day));
    }

    /// <summary>The local date the goal was created on.</summary>
    public DateOnly CreatedOn(TimeZoneInfo zone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(CreatedAt, zone).DateTime);
    }

    /// <summary>Whether <paramref name="day"/> is part of the goal's life: on or after the day it was created, and not paused.</summary>
    public bool IsAliveOn(DateOnly day, TimeZoneInfo zone)
    {
        return day >= CreatedOn(zone) && !IsPausedOn(day);
    }
}
