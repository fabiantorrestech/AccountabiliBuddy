using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What the quick-session popup starts with: the last choices made in it, plus its global shortcut.
/// App-owned (not part of the shared profile schema).
/// </summary>
/// <param name="ProfileId">Last profile used; null = first profile.</param>
public sealed record QuickSessionPreferences(
    Guid? ProfileId,
    int FocusMinutes,
    bool BreakEnabled,
    int BreakMinutes,
    string Hotkey)
{
    public static QuickSessionPreferences Default { get; } =
        new(null, FocusMinutes: 25, BreakEnabled: false, BreakMinutes: 5, HotkeyChord.Default.ToString());
}
