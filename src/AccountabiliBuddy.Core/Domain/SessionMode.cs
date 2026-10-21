namespace AccountabiliBuddy.Core.Domain;

public enum SessionMode
{
    Manual,
    Pomodoro,
    Scheduled,

    /// <summary>A past focus session the user added by hand in History (ADR 20); no activity was recorded for it.</summary>
    Added,
}
