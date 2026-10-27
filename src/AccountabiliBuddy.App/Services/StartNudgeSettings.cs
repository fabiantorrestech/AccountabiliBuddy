namespace AccountabiliBuddy.App.Services;

/// <summary>Whether the app steers toward starting during work parts with no session (<see cref="StartNudgeService"/>).</summary>
/// <param name="RepeatMinutes">Show the card again this often (5, 10 or 15) until a session starts; 0 = one card. 15 by default.</param>
/// <param name="Version">0 = saved before the card (its repeat only flashed the taskbar); then it becomes 15 once.</param>
/// <param name="AssistantOn">The first arrival of a workday opens the start-of-day assistant (hours, first tasks, Start) in place of the card (<see cref="StartOfDayAssistant"/>); on by default, and only while <paramref name="On"/> is.</param>
public sealed record StartNudgeSettings(bool On = true, int RepeatMinutes = 15, int Version = 0, bool AssistantOn = true)
{
    public const int CurrentVersion = 1;

    public static StartNudgeSettings Default { get; } = new(Version: CurrentVersion);
}
