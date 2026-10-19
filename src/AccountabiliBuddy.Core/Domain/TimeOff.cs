using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// Planned time off on one date (schema v4): all day, or only from <see cref="Start"/> to <see cref="End"/>.
/// No scheduled block runs during it; a block that overlaps it runs only before and after.
/// If <see cref="End"/> is not after <see cref="Start"/>, it runs past midnight into the next day.
/// </summary>
public sealed record TimeOff(Guid Id, DateOnly Date, TimeOnly? Start = null, TimeOnly? End = null, string? Note = null)
{
    // Derived from Start/End; not part of the shared JSON schema.
    [JsonIgnore]
    public bool IsAllDay => Start is null || End is null;

    /// <summary>The local start (inclusive) and end (exclusive) of this time off.</summary>
    public LocalWindow GetWindow()
    {
        DateTime day = Date.ToDateTime(TimeOnly.MinValue);
        if (IsAllDay)
        {
            return new LocalWindow(day, day.AddDays(1));
        }

        DateTime start = day + Start!.Value.ToTimeSpan();
        DateTime end = day + End!.Value.ToTimeSpan();
        return new LocalWindow(start, end > start ? end : end.AddDays(1));
    }
}
