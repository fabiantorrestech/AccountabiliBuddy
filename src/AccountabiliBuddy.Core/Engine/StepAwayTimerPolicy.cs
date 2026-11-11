using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The rules of the "When I step away" setting (ADR 77). Keeping the timer running keeps blocking on while away, so it is the stricter
/// choice: switching to it applies at once; switching to pausing the timer (blocking held while away) is looser, so it waits like any
/// looser change and is refused during a session. Only a plain step away (a break or an interruption) follows the setting: lunch, a
/// side task and "Pause timer" keep their own rules.
/// </summary>
public static class StepAwayTimerPolicy
{
    public static StepAwayTimer Default => StepAwayTimer.KeepTimerRunning;

    /// <summary>True when going from <paramref name="from"/> to <paramref name="to"/> loosens the session (pausing the timer while away).</summary>
    public static bool IsLooser(StepAwayTimer from, StepAwayTimer to)
    {
        return from == StepAwayTimer.KeepTimerRunning && to == StepAwayTimer.PauseTimer;
    }

    /// <summary>The stricter of two settings (keeping the timer running wins): a running session never steps away looser than it started.</summary>
    public static StepAwayTimer Stricter(StepAwayTimer first, StepAwayTimer second)
    {
        return first == StepAwayTimer.KeepTimerRunning || second == StepAwayTimer.KeepTimerRunning ? StepAwayTimer.KeepTimerRunning : StepAwayTimer.PauseTimer;
    }

    /// <summary>Whether a step away of <paramref name="kind"/> keeps the timer running under <paramref name="setting"/>.</summary>
    public static bool KeepsTimerRunning(StepAwayTimer setting, AwayKind kind)
    {
        return setting == StepAwayTimer.KeepTimerRunning && kind is AwayKind.Break or AwayKind.Interrupted;
    }
}
