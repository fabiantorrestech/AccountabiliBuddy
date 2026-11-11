namespace AccountabiliBuddy.Core.Tasks;

/// <summary>The day of a snooze shortcut (ADR 76): a day plus a time, e.g. Today 6:00 PM, Tomorrow 9:00 AM, Next Monday 9:00 AM.</summary>
public enum SnoozeDay
{
    Today,
    Tomorrow,

    /// <summary>The next Monday after today.</summary>
    NextMonday,

    /// <summary>The next time that weekday comes (today when the time is still ahead).</summary>
    Weekday,
}
