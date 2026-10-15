namespace AccountabiliBuddy.Core.Domain;

/// <param name="TotalCycles">Focus cycles before the session ends; null runs until stopped.</param>
public sealed record PomodoroSettings(
    TimeSpan FocusLength,
    TimeSpan ShortBreakLength,
    TimeSpan LongBreakLength,
    int CyclesBeforeLongBreak,
    int? TotalCycles = null)
{
    public static PomodoroSettings Default { get; } = new(
        FocusLength: TimeSpan.FromMinutes(25),
        ShortBreakLength: TimeSpan.FromMinutes(5),
        LongBreakLength: TimeSpan.FromMinutes(15),
        CyclesBeforeLongBreak: 4);
}
