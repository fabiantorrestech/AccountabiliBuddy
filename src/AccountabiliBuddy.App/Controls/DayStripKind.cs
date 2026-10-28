namespace AccountabiliBuddy.App.Controls;

/// <summary>What a stretch on the Stats page's day strip is.</summary>
public enum DayStripKind
{
    /// <summary>Counted deep work (Ab.Met).</summary>
    Focus,

    /// <summary>Off track during a session (Ab.Drift).</summary>
    Drift,

    /// <summary>Asleep (Ab.Sleep).</summary>
    Sleep,
}
