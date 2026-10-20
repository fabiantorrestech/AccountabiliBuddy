namespace AccountabiliBuddy.App.Services;

/// <summary>A row of the day bar and the on-screen timeline (stored as its number in the "daybar" document).</summary>
public enum TimelineRow
{
    /// <summary>Events from the calendars chosen for the timeline.</summary>
    Calendar,

    /// <summary>The day plan's parts in their category colours.</summary>
    Plan,

    /// <summary>Focus sessions you started, with drift marks.</summary>
    Focus,

    /// <summary>What the blockers block (Cold Turkey's blocks).</summary>
    Blockers,
}
