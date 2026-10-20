namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// Everything the session engine needs to carry on with a session after the app restarts
/// (crash, killed from Task Manager, PC restarted).
/// </summary>
/// <param name="Pomodoro">The running Pomodoro's settings; null for other modes.</param>
/// <param name="BreakAfterLastCycle">Whether the Pomodoro also runs the break after its last focus cycle.</param>
public sealed record SessionCheckpoint(
    SessionState State,
    PomodoroSettings? Pomodoro,
    bool BreakAfterLastCycle);
