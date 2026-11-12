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

    /// <summary>A warning: overdue, a sync problem.</summary>
    Drift,
}
