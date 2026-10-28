namespace AccountabiliBuddy.App.Controls;

/// <summary>A stretch on the day strip, as parts of the day from midnight (0) to the next midnight (1).</summary>
/// <param name="Tip">Shown on hover, e.g. "Focus 9:32 AM – 10:20 AM".</param>
public readonly record struct DayStripSpan(double From, double To, DayStripKind Kind, string Tip);
