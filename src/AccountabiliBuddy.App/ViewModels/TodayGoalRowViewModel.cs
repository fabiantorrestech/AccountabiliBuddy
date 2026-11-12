using Avalonia.Media;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One goal on the Today sidebar's "Today's goals" (round 2, brief §4.2): its icon on a chip, its name, the one number the Goals list
/// shows ("8 / 15 min"), and an amber Behind pill. At the right, the kind's control (board B-PC-Idle): a habit its tick circle ("Mark
/// done today", filled once today is hit; a click unticks), an amount goal a Log button, the others the small bar. Deep work keeps its
/// pencil (the daily goal's edit, unchanged).
/// </summary>
public sealed record TodayGoalRowViewModel(
    Guid Id,
    string Name,
    MaterialIconKind Icon,
    IBrush ColourBrush,
    string Number,
    double Fraction,
    bool ShowsBar,
    bool IsBehind,
    bool IsDeepWork,
    bool CanTick = false,
    bool IsHit = false,
    bool CanLog = false)
{
    /// <summary>The tick circle's tooltip and name.</summary>
    public string TickTip => IsHit ? "Done today · click to untick" : "Mark done today";

    /// <summary>The Log button's name ("Log Weight").</summary>
    public string LogName => "Log " + Name;
}
