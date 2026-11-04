namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The views of the Stats page, in the order of its chips.</summary>
public enum StatsViewKind
{
    Overview,
    Consistency,
    Intensity,
    FocusGraph,
    Drift,
    Sleep,
    Phone,
    Steps,

    /// <summary>"How did today go" from the shutdown routine: the feeling and note per day over the period (sealed on this PC).</summary>
    Reviews,
}
