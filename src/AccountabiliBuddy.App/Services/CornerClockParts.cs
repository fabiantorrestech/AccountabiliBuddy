namespace AccountabiliBuddy.App.Services;

/// <summary>What the corner clock shows over a full-screen app.</summary>
[Flags]
public enum CornerClockParts
{
    None = 0,
    Session = 1,
    Time = 2,
    Date = 4,
    Battery = 8,
}
