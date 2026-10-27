namespace AccountabiliBuddy.Core.Domain;

public enum SessionPhase
{
    Idle,
    Focus,
    ShortBreak,
    LongBreak,

    /// <summary>
    /// A session's planning stage, before its first focus countdown (<see cref="SessionState.IsPlanning"/>). Enforcement runs as in
    /// focus, but it is not focus time. Appended last: the checkpoint stores the phase as a number.
    /// </summary>
    Planning,
}
