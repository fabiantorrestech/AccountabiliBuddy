namespace AccountabiliBuddy.App.Services.Navigation;

/// <summary>What a swipe is doing after an input: its phase, the way it points and how far it has got (0 to 1, 1 = the threshold).</summary>
public readonly record struct SwipeState(SwipePhase Phase, HistoryDirection Direction, double Progress)
{
    public static SwipeState Idle { get; } = new(SwipePhase.Idle, HistoryDirection.Back, 0);
}
