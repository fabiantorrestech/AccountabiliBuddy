using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// One stretch of a day template, e.g. Work 09:00–12:00. A part whose <see cref="End"/> is not after its
/// <see cref="Start"/> (Sleep 23:45–06:45) belongs to the day it ends on and starts the evening before, so the Workday
/// template's Sleep is the night before a workday. An <see cref="End"/> of 00:00 means midnight at the end of the
/// part's own day (Extra Work 22:00–00:00), not the evening before.
/// </summary>
/// <param name="Label">Shown instead of the category's name, e.g. "After-office work"; null = the category's name.</param>
public sealed record PlanPart(Guid Id, Guid CategoryId, TimeOnly Start, TimeOnly End, string? Label = null)
{
    // Derived from Start/End; not part of the shared JSON schema.
    [JsonIgnore]
    public bool EndsAtMidnight => End == TimeOnly.MinValue;

    [JsonIgnore]
    public bool CrossesMidnight => !EndsAtMidnight && End <= Start;

    /// <summary>The end as minutes after the part's own midnight start of day (1440 for an end at midnight).</summary>
    [JsonIgnore]
    public int EndMinutes => EndsAtMidnight ? 24 * 60 : (int)End.ToTimeSpan().TotalMinutes;
}
