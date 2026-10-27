namespace AccountabiliBuddy.Core.Domain;

/// <summary>How a day template says its day is guarded (schema v12, ADR 47).</summary>
public enum DayMode
{
    /// <summary>Timed parts, or just a <see cref="WorkWindow"/>; the work parts are what the guards watch.</summary>
    Schedule,

    /// <summary>A number of productive minutes; the parts are only the day's non-productive windows (rest, sleep, free) and Pre-Work.</summary>
    Budget,

    /// <summary>No guard at all.</summary>
    Free,
}
