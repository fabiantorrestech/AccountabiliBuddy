namespace AccountabiliBuddy.Core.Domain;

/// <summary>What an armed end does once its wait is over (ADR 66).</summary>
public enum ArmedEndKind
{
    /// <summary>Ends the running focus session early, with the reason given.</summary>
    EndSession,

    /// <summary>Ends the work block early (Start shutdown, ADR 65), then the shutdown routine opens.</summary>
    EndWorkEarly,

    /// <summary>The work-hours guard stops for today (ADR 27).</summary>
    StopForToday,

    /// <summary>Pre-Work's "No, day off": today becomes a day off on this computer.</summary>
    PreWorkDayOff,
}
