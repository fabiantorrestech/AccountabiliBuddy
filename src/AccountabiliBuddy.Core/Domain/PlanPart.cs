using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// One stretch of a day template, e.g. Work 09:00–12:00. A part whose <see cref="End"/> is not after its
/// <see cref="Start"/> (Sleep 23:45–06:45) belongs to the day it ends on and starts the evening before, so the Workday
/// template's Sleep is the night before a workday. An <see cref="End"/> of 00:00 means midnight at the end of the
/// part's own day (Extra Work 22:00–00:00), not the evening before.
/// </summary>
/// <param name="Label">Shown instead of the category's name, e.g. "After-office work"; null = the category's name.</param>
/// <param name="FlexMinutes">
/// A flexible break (schema v8, e.g. lunch): Start–End is the window it may start in, this is how long it lasts once
/// started ("Start lunch", on any device). Until then the window counts as the work around it. Null = fixed hours.
/// </param>
/// <param name="HintAt">A flexible break not started by this time gets one hint; null = none.</param>
/// <param name="WakeByAlarm">
/// A Sleep part whose wake time is the wake-up alarm (schema v9, ADR 33): the morning's alarm when one is set, else
/// bedtime plus the sleep goal. Only its start (bed by) is the user's; its end is kept at bedtime plus the goal for
/// drawing. False = the end is the wake time.
/// </param>
public sealed record PlanPart(
    Guid Id,
    Guid CategoryId,
    TimeOnly Start,
    TimeOnly End,
    string? Label = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? FlexMinutes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TimeOnly? HintAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool WakeByAlarm = false)
{
    /// <summary>A flexible break: its times are a window, it lasts <see cref="FlexMinutes"/> once started.</summary>
    [JsonIgnore]
    public bool IsFlexible => FlexMinutes is > 0;

    // Derived from Start/End; not part of the shared JSON schema.
    [JsonIgnore]
    public bool EndsAtMidnight => End == TimeOnly.MinValue;

    [JsonIgnore]
    public bool CrossesMidnight => !EndsAtMidnight && End <= Start;

    /// <summary>The end as minutes after the part's own midnight start of day (1440 for an end at midnight).</summary>
    [JsonIgnore]
    public int EndMinutes => EndsAtMidnight ? 24 * 60 : (int)End.ToTimeSpan().TotalMinutes;
}
