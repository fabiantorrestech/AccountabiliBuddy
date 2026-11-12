using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One thing that counts toward a goal (brief §5.1, "Tracking &amp; sources"). Sources add up; a minute counts once per goal
/// even when several of its rules match it (<see cref="GoalCounting"/>). Only the fields of its <see cref="Type"/> are set.
/// </summary>
/// <param name="LabelId">FocusLabel: the label's id (<see cref="Domain.TaskLabel.IdFor"/> its name).</param>
/// <param name="Tag">FocusTag: the #tag, compared case-insensitively (<see cref="Domain.TaskLabels.Same"/>). Private text of the name class: sealed at rest with the device's key, never logged; a name: in a backup it travels readable like a task title.</param>
/// <param name="CalendarCategoryId">CalendarCategory: the ADR 58 category whose events count.</param>
/// <param name="App">ScreenTime: the app or category name. Private text of the name class: sealed at rest with the device's key, never logged; a name: in a backup it travels readable like a task title.</param>
/// <param name="ScreenDirection">ScreenTime: at most or at least.</param>
/// <param name="BeeminderSlug">Beeminder: the linked goal's slug.</param>
/// <param name="BeeminderDirection">Beeminder: Read only until ADR 80 (Send and Both are "Coming soon").</param>
/// <param name="ThresholdMinutes">"Ticks itself at 15 min": a habit's day is hit once the time sources reach it; null = only a hand tick.</param>
/// <param name="Automatic">
/// Whether the source ticks and logs by itself (brief §5.10: on by default in AccountabiliBuddy mode, the user's choice per
/// source in ProductaBuddy mode). Off: it is listed but counts nothing.
/// </param>
public sealed record GoalSourceRule(
    Guid Id,
    GoalSourceType Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? LabelId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CalendarCategoryId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? App = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalDirection? ScreenDirection = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BeeminderSlug = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalBeeminderDirection? BeeminderDirection = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? ThresholdMinutes = null,
    bool Automatic = true)
{
    /// <summary>Whether the rule counts time (focus or calendar windows) rather than readings or ticks.</summary>
    [JsonIgnore]
    public bool CountsTime => Type is GoalSourceType.FocusAll or GoalSourceType.FocusLabel or GoalSourceType.FocusTag or GoalSourceType.CalendarCategory;
}
