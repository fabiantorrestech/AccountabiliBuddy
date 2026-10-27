namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A Schedule day's simple work window (schema v12, ADR 47), e.g. 9:00 AM–5:00 PM: when the template has no work parts
/// of its own, the window is its work part. An <see cref="End"/> of 00:00 means midnight at the end of the day.
/// </summary>
public sealed record WorkWindow(TimeOnly Start, TimeOnly End);
