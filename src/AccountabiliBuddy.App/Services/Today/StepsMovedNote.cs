namespace AccountabiliBuddy.App.Services.Today;

/// <summary>
/// Where the one-time "Steps now live in Goals" card is (TD1): not yet decided, to be shown (steps were on Today), or finished (added,
/// dismissed, or steps were never on Today here). Decided once when the saved layout is first read by this version.
/// </summary>
public enum StepsMovedNote
{
    NotChecked = 0,
    Show = 1,
    Done = 2,
}
