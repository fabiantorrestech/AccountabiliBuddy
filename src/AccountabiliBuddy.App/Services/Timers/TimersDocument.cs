namespace AccountabiliBuddy.App.Services.Timers;

/// <summary>What document "timers" keeps: this PC's timers and alarms (V1: never sent anywhere).</summary>
public sealed record TimersDocument(IReadOnlyList<TimerItem> Timers, IReadOnlyList<AlarmItem> Alarms)
{
    public static TimersDocument Empty { get; } = new([], []);
}
