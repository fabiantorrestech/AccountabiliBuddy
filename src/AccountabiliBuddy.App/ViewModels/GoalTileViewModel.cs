using AccountabiliBuddy.Core.Goals;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One tile of the goal detail (round 2, change note §4a "Tiles, left to right by time horizon"): Today, This period, Target (only
/// with a target or a date), and the optional Streak and Total. A big number with "of 15", a caption, a bar or the period's segments,
/// a foot line, an On track / Behind pill, the pending-change line with Cancel, and (Target) the What it takes link.
/// </summary>
public sealed partial class GoalTileViewModel : ObservableObject
{
    public GoalTileViewModel(string kind)
    {
        Kind = kind;
    }

    /// <summary>"today", "period", "target", "streak", "total" (the tile's name in the window: "GoalTile-today").</summary>
    public string Kind { get; }

    public bool IsTarget => Kind == "target";

    /// <summary>The tile's accessible name ("Target tile"); its values are in <see cref="AccessibleName"/> (help text).</summary>
    public string TileName => Kind switch
    {
        "today" => "Today tile",
        "period" => "This period tile",
        "target" => "Target tile",
        "streak" => "Streak tile",
        _ => "Total tile",
    };

    [ObservableProperty]
    public partial MaterialIconKind Icon { get; set; }

    [ObservableProperty]
    public partial IBrush? ColourBrush { get; set; }

    /// <summary>"8", "34h".</summary>
    [ObservableProperty]
    public partial string Big { get; set; } = string.Empty;

    /// <summary>"of 15", "of 50h".</summary>
    [ObservableProperty]
    public partial string? Of { get; set; }

    /// <summary>"Minutes today", "Days this week · on pace", "Target · by Sun 1/31".</summary>
    [ObservableProperty]
    public partial string Caption { get; set; } = string.Empty;

    /// <summary>"7 min to go · ticks itself at 15", "done ~1/10".</summary>
    [ObservableProperty]
    public partial string? Foot { get; set; }

    [ObservableProperty]
    public partial bool ShowsBar { get; set; }

    [ObservableProperty]
    public partial double Fraction { get; set; }

    /// <summary>"Where you should be today" (Target), 0 to 1; NaN for none.</summary>
    [ObservableProperty]
    public partial double Mark { get; set; } = double.NaN;

    [ObservableProperty]
    public partial IReadOnlyList<GoalStripCell>? Segments { get; set; }

    /// <summary>"On track", "Behind".</summary>
    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial bool IsStatusAmber { get; set; }

    /// <summary>"Lowers to 1h 30m at 2:14 PM" (a waiting Deep work change).</summary>
    [ObservableProperty]
    public partial string? Pending { get; set; }

    [ObservableProperty]
    public partial IAsyncRelayCommand? CancelPendingCommand { get; set; }

    /// <summary>The What it takes pop-up (the Target tile only; null on the others, so only that tile's pop-up can open).</summary>
    [ObservableProperty]
    public partial GoalWhatItTakesViewModel? WhatItTakes { get; set; }

    public bool ShowsSegments => Segments is { Count: > 0 };

    public bool HasOf => !string.IsNullOrEmpty(Of);

    public bool HasFoot => !string.IsNullOrEmpty(Foot);

    public bool HasStatus => Status is not null;

    public bool IsStatusTeal => Status is not null && !IsStatusAmber;

    public bool HasPending => Pending is not null;

    /// <summary>"Today, 8 of 15, Minutes today".</summary>
    public string AccessibleName => $"{Caption}, {Big} {Of}".Trim();

    /// <summary>Raises the derived values after an update.</summary>
    public void Changed()
    {
        OnPropertyChanged(nameof(ShowsSegments));
        OnPropertyChanged(nameof(HasOf));
        OnPropertyChanged(nameof(HasFoot));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(IsStatusTeal));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(AccessibleName));
    }
}
