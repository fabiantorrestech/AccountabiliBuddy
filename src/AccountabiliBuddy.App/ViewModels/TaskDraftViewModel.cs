using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A new task in the task editor popup (Tasks redesign item 5, SPEC "Task editor popup", board 08): opened from the add box's Details
/// (or Ctrl+Enter while typing) with what was typed: the title, the "@" label and "#" tags. The bin pill picks where it goes, and that bin
/// is the add box's own "Add to [bin]" (one value: <see cref="AddBoxViewModel.Destination"/>). Steps, notes, the time limit, the highlight
/// and the due date (Next and Later only, as for a task that exists) are kept here until "Add to [bin]" or Ctrl+Enter adds it as one
/// action; Cancel or Esc goes back to the add box, which still holds its text and chips. Titles, steps and notes are the user's text:
/// never logged.
/// </summary>
public sealed partial class TaskDraftViewModel : ObservableObject, IDisposable
{
    private readonly TasksViewModel _owner;
    private readonly AddBoxViewModel _box;
    private readonly List<string> _tags;

    public TaskDraftViewModel(TasksViewModel owner, AddBoxViewModel box, string title, Guid? labelId, IReadOnlyList<string> tags, int? estimateMinutes)
    {
        _owner = owner;
        _box = box;
        _tags = tags.ToList();
        Title = title;
        Note = NewStepText = CustomEstimateText = string.Empty;
        LabelId = labelId;
        EstimateMinutes = estimateMinutes;
        _box.PropertyChanged += OnBoxPropertyChanged;
    }

    /// <summary>The title, from the add box's text; changed here, it does not change the box.</summary>
    [ObservableProperty]
    public partial string Title { get; set; }

    // ---- the pill row ----

    /// <summary>Where it goes: the add box's own destination (its "Add to [bin]" button follows a pick here, and the other way round).</summary>
    public TaskBin Bin => _box.Destination;

    /// <summary>The primary button: "Add to To sort".</summary>
    public string AddText => _box.DestinationText;

    /// <summary>The bin pill's pick: the add box's destination changes with it.</summary>
    [RelayCommand]
    private void PickBin(TaskBin bin) => _box.Destination = bin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LabelName), nameof(LabelBrush))]
    public partial Guid? LabelId { get; set; }

    /// <summary>The label pill: the label's full name, or "No label".</summary>
    public string LabelName => _owner.LabelNameOf(LabelId) ?? "No label";

    public IBrush LabelBrush => _owner.LabelBrushOf(LabelId);

    /// <summary>The tags pill: "# + Tag" with none, else "#phone #home".</summary>
    public string TagText => _tags.Count == 0 ? "# + Tag" : string.Join(" ", _tags.Select(tag => "#" + tag));

    public IReadOnlyList<string> Tags => _tags;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeLimitText), nameof(StepsCaption), nameof(HasTimeLimit))]
    public partial int? EstimateMinutes { get; set; }

    public bool HasTimeLimit => EstimateMinutes is not null;

    /// <summary>The time limit pill: "25 min", or "Time limit" for none.</summary>
    public string TimeLimitText => EstimateMinutes is int minutes ? EstimateText.Of(minutes) : "Time limit";

    [ObservableProperty]
    public partial bool IsHighlighted { get; set; }

    [RelayCommand]
    private void ToggleHighlight() => IsHighlighted = !IsHighlighted;

    // ---- the body ----

    /// <summary>"Steps · no time limit", or "Steps · 25 min".</summary>
    public string StepsCaption => "Steps · " + (EstimateMinutes is int minutes ? EstimateText.Of(minutes) : "no time limit");

    public ObservableCollection<TaskDraftStepViewModel> Steps { get; } = [];

    [ObservableProperty]
    public partial string NewStepText { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    /// <summary>Due shows only for Next and Later, as it does for a task that exists (the old Later list's tasks).</summary>
    public bool ShowsDue => Bin is TaskBin.Next or TaskBin.Later;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDue))]
    public partial DateTime? DueDate { get; set; }

    public bool HasDue => DueDate is not null;

    [ObservableProperty]
    public partial bool IsCustomEstimateOpen { get; set; }

    [ObservableProperty]
    public partial string CustomEstimateText { get; set; }

    /// <summary>The time limit pill's menu: 10, 15, 25, 50, 90 min, No limit and Custom… (the chosen one ticked).</summary>
    public IReadOnlyList<TaskMenuEntry> TimeLimitMenu()
    {
        var entries = new List<TaskMenuEntry>();
        foreach (int minutes in new[] { 10, 15, 25, 50, 90 })
        {
            int chosen = minutes;
            entries.Add(new TaskMenuEntry(EstimateText.Of(minutes), new RelayCommand(() => EstimateMinutes = chosen), IsChecked: EstimateMinutes == minutes,
                Icon: MaterialIconKind.TimerOutline));
        }

        entries.Add(new TaskMenuEntry("No limit", new RelayCommand(() => EstimateMinutes = null), IsChecked: EstimateMinutes is null, Icon: MaterialIconKind.TimerOffOutline));
        entries.Add(TaskMenuEntry.Do("Custom…", OpenCustomEstimate, MaterialIconKind.PencilOutline));
        return entries;
    }

    /// <summary>The tags pill's menu: the tags this device shows (ticked where picked) and "New tag…".</summary>
    public IReadOnlyList<TaskMenuEntry> TagMenu() => _owner.DraftTagMenu(_tags, ToggleTag, AddTag);

    /// <summary>The label pill's picker, on the label picked here.</summary>
    public LabelPickerViewModel? LabelPicker() => _owner.LabelPickerOf(LabelId);

    /// <summary>A pick in the label picker: a label, or "No label"; nothing picked leaves it.</summary>
    public void ApplyLabelPick(LabelPick pick)
    {
        if (!pick.IsNothing)
        {
            LabelId = pick.LabelId;
        }
    }

    [RelayCommand]
    private void AddStep()
    {
        string title = NewStepText.Trim();
        NewStepText = string.Empty;
        if (title.Length > 0)
        {
            Steps.Add(new TaskDraftStepViewModel(title, TaskStepLetters.Of(Steps.Count), RemoveStep));
        }
    }

    [RelayCommand]
    private void ClearDue() => DueDate = null;

    [RelayCommand]
    private void OpenCustomEstimate()
    {
        CustomEstimateText = EstimateMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        IsCustomEstimateOpen = true;
    }

    [RelayCommand]
    private void ApplyCustomEstimate()
    {
        IsCustomEstimateOpen = false;
        if (int.TryParse(CustomEstimateText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) && minutes > 0)
        {
            EstimateMinutes = SessionChecklist.CleanEstimate(minutes);
        }
    }

    [RelayCommand]
    private void CancelCustomEstimate() => IsCustomEstimateOpen = false;

    // ---- the footer ----

    /// <summary>"Add to [bin]" or Ctrl+Enter: adds the task (a step still typed in its box comes along).</summary>
    [RelayCommand]
    private Task AddAsync()
    {
        AddStep();
        return _owner.AddDraftAsync(this);
    }

    /// <summary>Cancel or Esc: back to the add box, its text and chips kept.</summary>
    [RelayCommand]
    private void Cancel() => _owner.CancelDraft();

    /// <summary>The due date, only where it shows.</summary>
    public DateOnly? DueOn => ShowsDue && DueDate is DateTime date ? DateOnly.FromDateTime(date) : null;

    public void Dispose()
    {
        _box.PropertyChanged -= OnBoxPropertyChanged;
    }

    private void ToggleTag(string tag)
    {
        if (TaskLabels.Has(_tags, tag))
        {
            _tags.RemoveAll(held => string.Equals(held, tag, StringComparison.OrdinalIgnoreCase));
        }
        else if (TaskLabels.Clean([tag]) is [string clean])
        {
            _tags.Add(clean);
        }

        OnPropertyChanged(nameof(TagText));
        OnPropertyChanged(nameof(Tags));
    }

    private void AddTag(string tag)
    {
        if (!TaskLabels.Has(_tags, tag))
        {
            ToggleTag(tag);
        }
    }

    private void RemoveStep(TaskDraftStepViewModel step)
    {
        Steps.Remove(step);
        List<string> titles = Steps.Select(item => item.Title).ToList();
        Steps.Clear();
        foreach (string title in titles)
        {
            Steps.Add(new TaskDraftStepViewModel(title, TaskStepLetters.Of(Steps.Count), RemoveStep));
        }
    }

    private void OnBoxPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AddBoxViewModel.Destination))
        {
            OnPropertyChanged(nameof(Bin));
            OnPropertyChanged(nameof(AddText));
            OnPropertyChanged(nameof(ShowsDue));
        }
    }
}
