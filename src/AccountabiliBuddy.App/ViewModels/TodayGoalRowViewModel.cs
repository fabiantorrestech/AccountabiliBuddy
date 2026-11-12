using Avalonia.Media;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One goal on the Today sidebar's "Today's goals" (round 2, brief §4.2): its icon on a chip, its name, the one number the Goals list
/// shows ("8 / 15 min"), a small bar, and an amber Behind pill. Deep work keeps its pencil (the daily goal's edit, unchanged).
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
    bool IsDeepWork);
