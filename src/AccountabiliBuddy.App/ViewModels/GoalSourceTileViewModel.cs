using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One tile of Edit goal › Tracking &amp; sources › "Add a source" (round 2 R5.7, brief §6.1): an available source (Focus time on a label
/// or #tag, Tick by hand, Google Calendar, Health Connect, Pixel Watch through Health Connect, Screen time, Beeminder when connected), or a
/// greyed "Coming soon…" tile that can't be clicked and makes no connection (tooltip "Not available yet"): Garmin, Focusmate, Toggl,
/// RescueTime, GitHub, Strava, Webhook and CSV (the task goal link and milestone tasks became real tiles in GS5a).
/// </summary>
public sealed partial class GoalSourceTileViewModel : ObservableObject
{
    private readonly Action<GoalSourceTileViewModel>? _add;

    /// <param name="type">The source it adds; null for a placeholder with no source type yet.</param>
    /// <param name="add">Adds the source; null for a "Coming soon" tile (inert).</param>
    /// <param name="isStepsTile">SR1-P2 (ADR 91): one of the two steps tiles (Health Connect, Pixel Watch). They share one steps source per goal; the tile whose
    /// <paramref name="stepsFrom"/> the source counts is "Selected", the other says "Use this" and switches it.</param>
    /// <param name="stepsFrom">What the tile sets on the steps source: null = all (Health Connect), <see cref="GoalStepsFrom.Watch"/> (Pixel Watch).</param>
    public GoalSourceTileViewModel(string key, string title, string text, MaterialIconKind icon, GoalSourceType? type, Action<GoalSourceTileViewModel>? add, string? note = null,
        bool isStepsTile = false, GoalStepsFrom? stepsFrom = null)
    {
        IsStepsTile = isStepsTile;
        StepsFrom = stepsFrom;
        Key = key;
        Title = title;
        Text = text;
        Icon = icon;
        Type = type;
        _add = add;
        Note = note;
    }

    public string Key { get; }

    /// <summary>One of the two steps tiles (B10 of the SR1 plan check).</summary>
    public bool IsStepsTile { get; }

    /// <summary>What a tap sets on the goal's steps source (null = all); only for a steps tile.</summary>
    public GoalStepsFrom? StepsFrom { get; }

    public string Title { get; }

    public string Text { get; }

    public MaterialIconKind Icon { get; }

    public GoalSourceType? Type { get; }

    /// <summary>"Measured on your phone", "This PC only"; null for none.</summary>
    public string? Note { get; }

    public bool HasNote => Note is not null;

    /// <summary>Greyed, "Coming soon…", not clickable.</summary>
    public bool IsComingSoon => _add is null;

    public string Tooltip => IsComingSoon ? "Not available yet" : Unavailable ?? (IsStepsTile && IsAdded ? (IsSelected ? $"{Title} is selected" : $"Count steps from {Title}") : $"Add {Title}");

    /// <summary>Already counting (a source that can only be added once: Tick by hand, steps); the tile says "Added".</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool IsAdded { get; set; }

    /// <summary>A steps tile only: the goal's steps source counts what this tile stands for (a tap on it does nothing).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool IsSelected { get; set; }

    /// <summary>Deep work: every tile is locked (its sources stay all counted focus).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool IsLocked { get; set; }

    /// <summary>Why this goal can't take the source (GS5: milestone tasks only count on a milestone); null when it can. The tile is greyed with it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string? Unavailable { get; set; }

    // A steps tile stays tappable while another way of counting is selected (it switches the one source); every other tile can't be added twice.
    public bool CanAdd => !IsComingSoon && !(IsStepsTile ? IsAdded && IsSelected : IsAdded) && !IsLocked && Unavailable is null;

    // B10: "Add" with no steps source, then "Selected" / "Use this"; never "Added" on a steps tile.
    public string ButtonText => IsComingSoon ? "Coming soon…"
        : IsStepsTile && IsAdded ? (IsSelected ? "Selected" : "Use this")
        : IsAdded ? "Added" : Unavailable is not null ? "Milestones only" : "Add";

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        if (CanAdd)
        {
            _add?.Invoke(this);
        }
    }

    partial void OnIsAddedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(Tooltip));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(Tooltip));
    }

    partial void OnIsLockedChanged(bool value) => OnPropertyChanged(nameof(CanAdd));

    partial void OnUnavailableChanged(string? value)
    {
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(Tooltip));
    }
}
