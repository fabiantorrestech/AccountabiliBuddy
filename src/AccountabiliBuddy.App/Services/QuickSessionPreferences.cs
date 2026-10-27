using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What the quick-session popup starts with: the last choices made in it, plus its global shortcut.
/// App-owned (not part of the shared profile schema).
/// </summary>
/// <param name="ProfileId">Last profile used; null = first profile.</param>
/// <remarks><see cref="CountUpProfileId"/>: the profile "Quick Start with Default" starts (Settings); null = "QS - Default", made when missing.</remarks>
public sealed record QuickSessionPreferences(
    Guid? ProfileId,
    int FocusMinutes,
    bool BreakEnabled,
    int BreakMinutes,
    string Hotkey)
{
    public static QuickSessionPreferences Default { get; } =
        new(null, FocusMinutes: 25, BreakEnabled: false, BreakMinutes: 5, HotkeyChord.Default.ToString());

    public Guid? CountUpProfileId { get; init; }

    /// <summary>The test session's length in minutes (Today → More options); 1 by default.</summary>
    public int TestMinutes { get; init; } = 1;
}
