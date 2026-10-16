namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// A time range the user dragged out on the week grid.
/// An <see cref="End"/> of 00:00 means "until midnight".
/// </summary>
public sealed record ScheduleSlot(DayOfWeek Day, TimeOnly Start, TimeOnly End);
