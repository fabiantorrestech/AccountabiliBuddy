namespace AccountabiliBuddy.App.Services;

/// <summary>Whether the app steers toward starting during work parts with no session (<see cref="StartNudgeService"/>).</summary>
/// <param name="RepeatMinutes">Nudge again this often (5, 10 or 15) until a session starts; 0 = once per occasion. 5 by default.</param>
public sealed record StartNudgeSettings(bool On = true, int RepeatMinutes = 5)
{
    public static StartNudgeSettings Default { get; } = new();
}
