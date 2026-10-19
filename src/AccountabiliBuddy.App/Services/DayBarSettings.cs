namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The day bar at the top of the main window. App-owned (not part of the shared profile schema) and display only,
/// so changing it never loosens anything.
/// </summary>
/// <param name="DayEnd">Not after <paramref name="DayStart"/> = the day ends on the next date (e.g. 06:00 to 02:00).</param>
public sealed record DayBarSettings(bool Show, DayBarRange Range, TimeOnly DayStart, TimeOnly DayEnd)
{
    public static DayBarSettings Default { get; } = new(true, DayBarRange.FitToDay, new TimeOnly(6, 0), TimeOnly.MinValue);
}
