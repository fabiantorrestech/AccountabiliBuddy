using AccountabiliBuddy.App.Controls;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The right column's day: the strip, the two tiles and the list.</summary>
/// <param name="DeepWork">E.g. "2h 15m".</param>
/// <param name="DeepWorkNote">E.g. "goal met" or "45m to go".</param>
/// <param name="GoalMet">Shows the Met dot before the note.</param>
/// <param name="PhoneValue">E.g. "14"; null hides the Phone tile (no phone count for the day).</param>
public sealed record StatsDayPanel(
    IReadOnlyList<DayStripSpan> Spans,
    string DeepWork,
    string DeepWorkNote,
    bool GoalMet,
    string? PhoneValue,
    string PhoneNote,
    IReadOnlyList<StatsDayRow> Rows)
{
    public bool HasPhone => PhoneValue is not null;

    public bool HasRows => Rows.Count > 0;
}
