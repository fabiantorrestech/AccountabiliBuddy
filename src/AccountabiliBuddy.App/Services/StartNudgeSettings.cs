namespace AccountabiliBuddy.App.Services;

/// <summary>Whether the app steers toward starting during work parts with no session (<see cref="StartNudgeService"/>).</summary>
/// <param name="RepeatMinutes">Show the card again this often (5, 10 or 15) until a session starts; 0 = one card. 15 by default.</param>
public sealed record StartNudgeSettings(bool On = true, int RepeatMinutes = 15)
{
    public static StartNudgeSettings Default { get; } = new();
}
