using AccountabiliBuddy.App.Services;

namespace AccountabiliBuddy.App.Controls;

/// <summary>One labelled row of the day bar, e.g. Plan or Focus.</summary>
/// <param name="Label">Shown left of the row in the main window, e.g. "Focus 2h 55m".</param>
/// <param name="Marks">Drawn on top of the spans (drift on the Focus row).</param>
public sealed record DayBarRow(TimelineRow Kind, string Label, IReadOnlyList<DayBarSpan> Spans, IReadOnlyList<DayBarSpan> Marks);
