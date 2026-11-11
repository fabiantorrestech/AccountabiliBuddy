namespace AccountabiliBuddy.Core.Domain;

/// <summary>Why the user stepped away during a session. Whatever the kind, the countdown pauses until they're back.</summary>
public enum AwayKind
{
    /// <summary>A short break: bathroom, a drink.</summary>
    Break,

    /// <summary>Someone or something needed the user: a colleague, a call.</summary>
    Interrupted,

    /// <summary>
    /// Marked distracted afterwards in History (<see cref="SessionGap"/>); never written as a session event, only
    /// folded into a record by <see cref="Engine.SessionGaps.Apply"/>.
    /// </summary>
    Distracted,

    /// <summary>
    /// A small errand of 5-15 minutes done at the desk. The focus clock pauses like any away, but enforcement keeps running
    /// (<see cref="SessionState.PausesEnforcement"/>) and it is recorded apart as a side task, never as focus. Appended last:
    /// the checkpoint stores the kind as a number.
    /// </summary>
    SideTask,

    /// <summary>
    /// The session's planning stage (<see cref="SessionPhase.Planning"/>): enforcement ran but it is not focus. Never an engine
    /// away; only folded into a record by <see cref="Engine.SessionHistoryBuilder"/> from the planning events. Appended last.
    /// </summary>
    Planning,

    /// <summary>
    /// The PC was off, asleep or the app wasn't running during the session (ADR 60): non-work session time, never focus. Never an
    /// engine away; only folded into a record from the Interrupted/Resumed events. Appended last.
    /// </summary>
    DeviceOff,

    /// <summary>
    /// "Pause timer" (ADR 76, Tasks redesign): the countdown freezes until Resume like any away, and the session ends that much later,
    /// but enforcement keeps running as in focus (<see cref="SessionState.PausesEnforcement"/>) and input never asks "Back to the
    /// task?". Logged apart from stepping away. It never shortens a wait, ends the session or unlocks anything. Appended last.
    /// </summary>
    Paused,
}
