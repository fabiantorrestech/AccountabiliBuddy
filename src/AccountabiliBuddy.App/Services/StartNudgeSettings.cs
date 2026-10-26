namespace AccountabiliBuddy.App.Services;

/// <summary>Whether the app steers toward starting during work parts with no session (<see cref="StartNudgeService"/>).</summary>
/// <param name="RepeatMinutes">Show the card again this often (5, 10 or 15) until a session starts; 0 = one card. 15 by default.</param>
/// <param name="Version">0 = saved before the card (its repeat only flashed the taskbar); then it becomes 15 once.</param>
public sealed record StartNudgeSettings(bool On = true, int RepeatMinutes = 15, int Version = 0)
{
    public const int CurrentVersion = 1;

    public static StartNudgeSettings Default { get; } = new(Version: CurrentVersion);
}
