namespace AccountabiliBuddy.App.Services;

/// <summary>Whether the app steers toward starting during work parts with no session (<see cref="StartNudgeService"/>).</summary>
public sealed record StartNudgeSettings(bool On = true)
{
    public static StartNudgeSettings Default { get; } = new();
}
