using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One tile of Edit goal › Tracking &amp; sources › "Add a source" (round 2 R5.7, brief §6.1): an available source (Focus time on a label
/// or #tag, Tick by hand, Google Calendar, Health Connect, Pixel Watch through Health Connect, Screen time, Beeminder when connected), or a
/// greyed "Coming soon…" tile that can't be clicked and makes no connection (tooltip "Not available yet"): Garmin, Focusmate, Toggl,
/// RescueTime, GitHub, Strava, Webhook, CSV, and the task goal link / milestone tasks (plan §3b 2).
/// </summary>
public sealed partial class GoalSourceTileViewModel : ObservableObject
{
    private readonly Action<GoalSourceTileViewModel>? _add;

    /// <param name="type">The source it adds; null for a placeholder with no source type yet.</param>
    /// <param name="add">Adds the source; null for a "Coming soon" tile (inert).</param>
    public GoalSourceTileViewModel(string key, string title, string text, MaterialIconKind icon, GoalSourceType? type, Action<GoalSourceTileViewModel>? add, string? note = null)
    {
        Key = key;
        Title = title;
        Text = text;
        Icon = icon;
        Type = type;
        _add = add;
        Note = note;
    }

    public string Key { get; }

    public string Title { get; }

    public string Text { get; }

    public MaterialIconKind Icon { get; }

    public GoalSourceType? Type { get; }

    /// <summary>"Measured on your phone", "This PC only"; null for none.</summary>
    public string? Note { get; }

    public bool HasNote => Note is not null;

    /// <summary>Greyed, "Coming soon…", not clickable.</summary>
    public bool IsComingSoon => _add is null;

    public string Tooltip => IsComingSoon ? "Not available yet" : $"Add {Title}";

    /// <summary>Already counting (a source that can only be added once: Tick by hand, steps); the tile says "Added".</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool IsAdded { get; set; }

    /// <summary>Deep work: every tile is locked (its sources stay all counted focus).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool IsLocked { get; set; }

    public bool CanAdd => !IsComingSoon && !IsAdded && !IsLocked;

    public string ButtonText => IsComingSoon ? "Coming soon…" : IsAdded ? "Added" : "Add";

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
    }

    partial void OnIsLockedChanged(bool value) => OnPropertyChanged(nameof(CanAdd));
}
