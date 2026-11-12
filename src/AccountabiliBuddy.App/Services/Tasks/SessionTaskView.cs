namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>How This session lists a running session's actions (Tasks redesign `05` §2; Settings › Tasks › During a session).</summary>
public enum SessionTaskView
{
    /// <summary>Each action is a row: a focused step shows its letter chip and "↳ task · ● label · 142b ▾" (the default).</summary>
    Quiet,

    /// <summary>Each task is a heading with its steps: done ones folded into "done (a–b, d)", the next open ones greyed, "+N more steps".</summary>
    Grouped,
}
