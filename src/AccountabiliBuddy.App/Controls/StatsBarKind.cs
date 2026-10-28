namespace AccountabiliBuddy.App.Controls;

/// <summary>Which brush a bar on the Stats page's bar chart is drawn with.</summary>
public enum StatsBarKind
{
    /// <summary>The goal was met (Ab.Met).</summary>
    Met,

    /// <summary>Today, not met yet: progress (Ab.Accent).</summary>
    Accent,

    /// <summary>A day or hour that didn't reach the goal (Ab.Plan).</summary>
    Plan,

    /// <summary>Time off track (Ab.Drift).</summary>
    Drift,
}
