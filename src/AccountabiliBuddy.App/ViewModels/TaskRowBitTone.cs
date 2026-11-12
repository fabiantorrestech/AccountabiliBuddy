namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The colour of one piece of a task row's detail line.</summary>
public enum TaskRowBitTone
{
    /// <summary>The usual detail colour.</summary>
    Muted,

    /// <summary>Stands out: a tag chip, a due date that is today.</summary>
    Ink,

    /// <summary>Quieter: "untouched 5 wk".</summary>
    Faint,

    /// <summary>A warning: a sync problem.</summary>
    Drift,

    /// <summary>Late or behind (round 2, brief §3.8): an overdue due date, drawn as an amber pill.</summary>
    Amber,
}
