namespace AccountabiliBuddy.App.Services;

/// <summary>Step away (a bathroom break, an interruption). App-owned (documents "stepAway").</summary>
/// <param name="Hotkey">Global shortcut that steps away, or says "I'm back" while away; empty = none.</param>
/// <param name="AutoAfterIdle">Step away by itself after <paramref name="IdleMinutes"/> with no input during a session. Off by default.</param>
public sealed record StepAwaySettings(string Hotkey, bool AutoAfterIdle, int IdleMinutes)
{
    public static StepAwaySettings Default { get; } = new("Ctrl+Alt+B", AutoAfterIdle: false, IdleMinutes: 5);
}
