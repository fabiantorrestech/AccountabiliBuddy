using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A weekly blocked-time window, e.g. Monday 09:00–12:00 with the "Coding" profile.
/// If <see cref="End"/> is not after <see cref="Start"/>, the block runs past midnight into the next day.
/// </summary>
public sealed record ScheduleBlock(Guid Id, DayOfWeek Day, TimeOnly Start, TimeOnly End, Guid ProfileId)
{
    // Derived from Start/End; not part of the shared JSON schema.
    [JsonIgnore]
    public bool CrossesMidnight => End <= Start;

    /// <summary>Finds the occurrence of this block that contains <paramref name="localTime"/>, if any.</summary>
    public bool TryGetOccurrence(DateTime localTime, out DateTime occurrenceStart, out DateTime occurrenceEnd)
    {
        TimeOnly timeOfDay = TimeOnly.FromDateTime(localTime);
        DateTime today = localTime.Date;

        if (!CrossesMidnight)
        {
            if (localTime.DayOfWeek == Day && timeOfDay >= Start && timeOfDay < End)
            {
                occurrenceStart = today + Start.ToTimeSpan();
                occurrenceEnd = today + End.ToTimeSpan();
                return true;
            }
        }
        else if (localTime.DayOfWeek == Day && timeOfDay >= Start)
        {
            // Started today, ends tomorrow.
            occurrenceStart = today + Start.ToTimeSpan();
            occurrenceEnd = today.AddDays(1) + End.ToTimeSpan();
            return true;
        }
        else if (localTime.DayOfWeek == NextDay(Day) && timeOfDay < End)
        {
            // Started yesterday, ends today.
            occurrenceStart = today.AddDays(-1) + Start.ToTimeSpan();
            occurrenceEnd = today + End.ToTimeSpan();
            return true;
        }

        occurrenceStart = default;
        occurrenceEnd = default;
        return false;
    }

    private static DayOfWeek NextDay(DayOfWeek day)
    {
        return (DayOfWeek)(((int)day + 1) % 7);
    }
}
