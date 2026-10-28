namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the Focus graph shows: the line series, the scale, the goal and the cards.</summary>
public sealed record StatsTrendPanel(
    IReadOnlyList<double?> Main,
    IReadOnlyList<double?>? Secondary,
    IReadOnlyList<string?> Labels,
    IReadOnlyList<string> Tips,
    double Maximum,
    double Goal,
    string GoalText,
    IReadOnlyList<StatsCard> Cards)
{
    public bool HasCards => Cards.Count > 0;
}
