namespace AccountabiliBuddy.App.Services.Today;

/// <summary>
/// The two parts of Today's main columns (TD1, owner 2026-11-13): the session card (or the start card) on the left and This session's
/// list on the right. Each can be hidden in either layout (<see cref="TodayLayoutPreferences.HiddenOutside"/>,
/// <see cref="TodayLayoutPreferences.HiddenInSession"/>). Stored as numbers; unknown bits are dropped on read.
/// </summary>
[Flags]
public enum TodayMainParts
{
    None = 0,
    SessionCard = 1,
    ThisSession = 2,
}
