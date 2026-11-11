namespace AccountabiliBuddy.Core.Tasks;

/// <summary>"During a focus session" (Settings › Alerts &amp; on-screen › Task reminders): what a normal reminder does while a session runs. Can't miss always rings.</summary>
public enum TaskReminderDuringSession
{
    /// <summary>Held until the session ends, then queued in due order (the default).</summary>
    WaitUntilEnd,

    /// <summary>Shown without taking the focus, the bell once, never full screen.</summary>
    RemindQuietly,
}
