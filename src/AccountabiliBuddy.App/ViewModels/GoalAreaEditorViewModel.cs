using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Edit area (round 2 R5.4, change note §4 "Areas: user-made"): name, icon, colour and its place among the areas, plus Delete, which asks
/// first and says where the area's goals go ("Its 3 goals move to Work"). The store refuses deleting an area that still holds a goal, so
/// the goals are moved to the chosen area first, then the area goes. Names are private text: on screen only, never logged. Applies at
/// once (areas feed no guard).
/// </summary>
public sealed partial class GoalAreaEditorViewModel : ObservableObject
{
    private static readonly string[] IconNames =
    [
        "work", "school", "fitness_center", "home", "self_improvement", "menu_book", "translate", "code", "music_note", "palette", "savings",
        "water_drop", "bedtime", "directions_run", "track_changes",
    ];

    private readonly GoalsService _goals;
    private readonly GoalArea? _area;

    /// <param name="area">The area to edit; null for a new one.</param>
    public GoalAreaEditorViewModel(GoalsService goals, GoalArea? area)
    {
        _goals = goals;
        _area = area;
        Icons = IconNames.Select(name => new GoalChoice(name, name.Replace('_', ' '), GoalIcons.Of(name))).ToList();
        Colours = TaskCategory.Presets.Select(colour => new GoalChoice(colour, colour, Brush: GoalIcons.Brush(colour))).ToList();
        List<GoalArea> live = goals.Lists.Areas.Where(item => item.IsLive).OrderBy(item => item.Position).ToList();
        int count = live.Count + (area is null ? 1 : 0);
        Positions = Enumerable.Range(1, count).Select(Ordinal).ToList();
        Name = area?.Name ?? string.Empty;
        SelectedIcon = Icons.FirstOrDefault(choice => choice.Key == area?.Icon)
            ?? (area is null ? Icons[^1] : new GoalChoice(area.Icon, area.Icon, GoalIcons.Of(area.Icon)));
        SelectedColour = Colours.FirstOrDefault(choice => string.Equals(choice.Key, area?.Color, StringComparison.OrdinalIgnoreCase))
            ?? (area is null ? Colours[0] : new GoalChoice(area.Color, area.Color, Brush: GoalIcons.Brush(area.Color)));
        PositionIndex = area is null ? count - 1 : Math.Max(0, live.FindIndex(item => item.Id == area.Id));
        GoalCount = area is null ? 0 : goals.Lists.LiveGoals.Count(goal => goal.AreaId == area.Id);
        foreach (GoalArea other in live.Where(item => item.Id != area?.Id))
        {
            Destinations.Add(new GoalChoice(other.Id.ToString(), other.Name, GoalIcons.Of(other.Icon), GoalIcons.Brush(other.Color)));
        }

        SelectedDestination = Destinations.FirstOrDefault(choice => choice.Key == GoalDefaults.WorkAreaId.ToString()) ?? Destinations.FirstOrDefault();
    }

    public event EventHandler? CloseRequested;

    public bool IsNew => _area is null;

    public bool CanDelete => !IsNew;

    public string Title => IsNew ? "New area" : "Edit area";

    public IReadOnlyList<GoalChoice> Icons { get; }

    public IReadOnlyList<GoalChoice> Colours { get; }

    public IReadOnlyList<string> Positions { get; }

    public ObservableCollection<GoalChoice> Destinations { get; } = [];

    /// <summary>The live goals in the area (Deep work included: it moves like any other).</summary>
    public int GoalCount { get; }

    /// <summary>The area made or edited by Save; null until then.</summary>
    public Guid? SavedId { get; private set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial GoalChoice? SelectedIcon { get; set; }

    [ObservableProperty]
    public partial GoalChoice? SelectedColour { get; set; }

    [ObservableProperty]
    public partial int PositionIndex { get; set; }

    [ObservableProperty]
    public partial GoalChoice? SelectedDestination { get; set; }

    /// <summary>Delete was pressed once: the question and the destination show; a second press deletes.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    public bool HasMessage => Message is not null;

    public bool HasGoals => GoalCount > 0;

    public bool NeedsDestination => HasGoals && Destinations.Count > 0;

    public string DeleteButtonText => IsConfirmingDelete ? "Delete area" : "Delete area…";

    /// <summary>"Its 3 goals move to Work. The area itself can't be brought back."</summary>
    public string DeleteText => !HasGoals
        ? "It holds no goals. The area itself can't be brought back."
        : Destinations.Count == 0
            ? $"Its {GoalTexts.Plural(GoalCount, "goal")} need another area to go to: make one first."
            : GoalCount == 1
                ? $"Its goal moves to {SelectedDestination?.Text ?? "another area"}. The area itself can't be brought back."
                : $"Its {GoalCount} goals move to {SelectedDestination?.Text ?? "another area"}. The area itself can't be brought back.";

    [RelayCommand]
    public async Task<bool> SaveAsync()
    {
        string name = Name.Trim();
        if (name.Length == 0)
        {
            Message = "Give the area a name.";
            return false;
        }

        if (name.Length > GoalEditorViewModel.MaxNameLength)
        {
            Message = $"Keep the name under {GoalEditorViewModel.MaxNameLength} characters.";
            return false;
        }

        Guid id = _area?.Id ?? Guid.NewGuid();
        string icon = SelectedIcon?.Key ?? "track_changes";
        string colour = SelectedColour?.Key ?? TaskCategory.Palette[0];
        double position = _area?.Position ?? (_goals.Lists.Areas.Where(item => item.IsLive).Select(item => item.Position).DefaultIfEmpty(0).Max() + 1);
        GoalArea saved = _area is null
            ? new GoalArea(id, name, icon, colour, position, default)
            : _area with { Name = name, Icon = icon, Color = colour };
        GoalChangeResult result = await _goals.SaveAreaAsync(saved, this);
        if (result != GoalChangeResult.Applied)
        {
            Message = result == GoalChangeResult.NotAvailable ? "Goals aren't ready on this PC yet." : "The area couldn't be saved.";
            return false;
        }

        await GoalAreaOrder.MoveAsync(_goals, id, PositionIndex, this);
        SavedId = id;
        CloseRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>First press: asks (and offers where the goals go). Second press: moves the goals, then deletes the area.</summary>
    [RelayCommand]
    public async Task<bool> DeleteAsync()
    {
        if (_area is null)
        {
            return false;
        }

        if (!IsConfirmingDelete)
        {
            IsConfirmingDelete = true;
            return false;
        }

        Guid destination = Guid.Empty;
        if (HasGoals && (SelectedDestination is null || !Guid.TryParse(SelectedDestination.Key, out destination)))
        {
            Message = "Make or pick another area for its goals first.";
            return false;
        }

        if (HasGoals)
        {
            double next = _goals.Lists.LiveGoals.Where(goal => goal.AreaId == destination).Select(goal => goal.Position).DefaultIfEmpty(0).Max();
            foreach (Goal goal in _goals.Lists.LiveGoals.Where(goal => goal.AreaId == _area.Id).OrderBy(goal => goal.Position).ToList())
            {
                next += 1;
                await _goals.UpdateGoalAsync(goal with { AreaId = destination, Position = next }, this);
            }
        }

        GoalChangeResult result = await _goals.DeleteAreaAsync(_area.Id, this);
        if (result != GoalChangeResult.Applied)
        {
            Message = "The area couldn't be deleted: a goal is still in it.";
            return false;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsConfirmingDelete)
        {
            IsConfirmingDelete = false;
            return;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedDestinationChanged(GoalChoice? value) => OnPropertyChanged(nameof(DeleteText));

    partial void OnIsConfirmingDeleteChanged(bool value) => OnPropertyChanged(nameof(DeleteButtonText));

    partial void OnMessageChanged(string? value) => OnPropertyChanged(nameof(HasMessage));

    private static string Ordinal(int number) => number switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{number}th",
    };
}
