namespace AccountabiliBuddy.App.Services.Navigation;

/// <summary>
/// How the swipe bubble looks for a swipe state (pure, so it can be checked): which edge it comes from, how far it has slid in,
/// how visible it is, and whether it is filled with the accent (at the threshold) or greyed (nowhere to go).
/// </summary>
public readonly record struct SwipeBubblePlan(HistoryDirection Direction, double Offset, double Opacity, bool Filled, bool Greyed)
{
    /// <summary>The bubble's diameter.</summary>
    public const double Size = 44;

    /// <summary>How far in from the page's edge it rests at the threshold.</summary>
    public const double EdgeInset = 12;

    /// <summary>
    /// The plan for a state. <paramref name="available"/> = there is a place to go that way. The offset is how far the bubble sits from
    /// its edge: hidden past the edge (<c>-Size</c>) at progress 0, <see cref="EdgeInset"/> at 1; opacity follows progress.
    /// </summary>
    public static SwipeBubblePlan For(SwipeState state, bool available)
    {
        double progress = Math.Clamp(state.Progress, 0, 1);
        double offset = -Size + (Size + EdgeInset) * progress;
        double opacity = available ? progress : progress * 0.55;
        return new SwipeBubblePlan(state.Direction, offset, opacity, available && progress >= 1, !available);
    }
}
