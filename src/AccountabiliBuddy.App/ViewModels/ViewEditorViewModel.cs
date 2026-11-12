using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Create a view / Edit view (spec 03 §C, 05 §10, prototype dialog "view"): a name (needed, and not one another view has), an icon,
/// Group by, Sort, Pin highlights and the "Show only" filters of <see cref="TaskViewConfig"/> (Labels incl. No label, Tags with any /
/// all, Due date, Time limit, Untouched for, Highlighted only). Create starts from what the Shelf shows now; "Use the Shelf's current
/// settings" copies them again. Saved through <see cref="TaskViewService.CreateAsync"/> / <see cref="TaskViewService.EditAsync"/> (synced
/// with the views). Edit offers Delete view for a view of the user's own (asks once). The name is the user's text: shown only.
/// </summary>
public sealed partial class ViewEditorViewModel : ObservableObject
{
    /// <summary>The icons a view can have (Material names, as the views store them).</summary>
    public static readonly IReadOnlyList<string> IconNames =
        ["bookmark-outline", "star-outline", "lightning-bolt", "head-cog", "briefcase-outline", "home-outline", "calendar-today", "fire", "leaf", "school-outline"];

    private static readonly (string Value, string Text)[] GroupChoices =
        [(TaskViewConfig.GroupBins, "Bins"), (TaskViewConfig.GroupLabel, "Label"), (TaskViewConfig.GroupTag, "Tag"), (TaskViewConfig.GroupNone, "None · one list")];

    // The "No label" tick's value among the label options.
    private const string NoLabelValue = "none";

    private readonly TaskViewService _views;
    private readonly TaskView? _editing;
    private readonly IReadOnlyList<TaskLabel> _labels;
    private readonly IReadOnlyList<string> _tags;
    private TaskViewConfig _base;

    /// <param name="editing">The view to edit; null to create one.</param>
    /// <param name="labels">The live labels, in order.</param>
    /// <param name="tags">The tags in use.</param>
    public ViewEditorViewModel(TaskViewService views, TaskView? editing, IReadOnlyList<TaskLabel> labels, IReadOnlyList<string> tags)
    {
        _views = views;
        _editing = editing;
        _labels = labels;
        _tags = tags;
        _base = editing?.Config ?? views.Working;
        NameProblem = string.Empty;
        Name = editing?.Name ?? string.Empty;
        Icon = editing?.Icon ?? IconNames[0];
        GroupNames = GroupChoices.Select(choice => choice.Text).ToList();
        SortNames = ShelfViewModel.SortChoices.Select(choice => choice.Text).ToList();
        Apply(_base);
    }

    public event EventHandler? CloseRequested;

    public bool IsEditing => _editing is not null;

    public string Heading => IsEditing ? "Edit view" : "Create a view";

    public string SaveText => IsEditing ? "Save changes" : "Save view";

    /// <summary>Delete view: only for a view of the user's own.</summary>
    public bool CanDelete => _editing is { BuiltIn: false };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteText))]
    public partial string Name { get; set; }

    /// <summary>"Give it a name" / "Another view has that name"; empty when the name is fine.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNameProblem))]
    public partial string NameProblem { get; set; }

    public bool HasNameProblem => NameProblem.Length > 0;

    [ObservableProperty]
    public partial string Icon { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ViewIconChoiceViewModel> Icons { get; set; } = [];

    public IReadOnlyList<string> GroupNames { get; }

    [ObservableProperty]
    public partial int GroupIndex { get; set; }

    public IReadOnlyList<string> SortNames { get; }

    [ObservableProperty]
    public partial int SortIndex { get; set; }

    [ObservableProperty]
    public partial bool PinHighlights { get; set; }

    [ObservableProperty]
    public partial bool HighlightedOnly { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ViewEditorOption> Labels { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ViewEditorOption> Tags { get; set; } = [];

    public bool HasTags => _tags.Count > 0;

    /// <summary>Tags: a task must have every ticked tag (else any of them).</summary>
    [ObservableProperty]
    public partial bool MatchAllTags { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ViewEditorOption> Due { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ViewEditorOption> Time { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ViewEditorOption> Old { get; set; } = [];

    /// <summary>Delete view was clicked once: the red confirm shows.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    public string DeleteText => "Delete “" + (_editing?.Name ?? Name) + "”";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>"Use the Shelf's current settings": everything below the name becomes what the Shelf shows now.</summary>
    [RelayCommand]
    private void TakeShelf()
    {
        _base = _views.Working;
        Apply(_base);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        string name = Name.Trim();
        NameProblem = ProblemWith(name);
        if (HasNameProblem)
        {
            return;
        }

        IsBusy = true;
        try
        {
            TaskViewConfig config = Config();
            if (_editing is TaskView view)
            {
                await _views.EditAsync(view.Id, name, Icon, config);
            }
            else if (await _views.CreateAsync(name, Icon, config) is null)
            {
                NameProblem = $"You have {TaskViewEdits.MaxViews} views, the most there can be. Delete one first.";
                return;
            }
        }
        finally
        {
            IsBusy = false;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSave() => !IsBusy;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void AskDelete() => IsConfirmingDelete = true;

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_editing is { BuiltIn: false } view)
        {
            await _views.DeleteAsync(view.Id);
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnNameChanged(string value)
    {
        if (HasNameProblem)
        {
            NameProblem = ProblemWith(value.Trim());
        }
    }

    partial void OnIconChanged(string value) => Icons = BuildIcons();

    // Needed, at most the stored length, and not the name of another live view (case does not matter).
    private string ProblemWith(string name)
    {
        if (name.Length == 0)
        {
            return "Give it a name";
        }

        string clean = TaskViewEdits.CleanName(name);
        bool taken = _views.Views.Any(view => view.Id != _editing?.Id && string.Equals(view.Name.Trim(), clean, StringComparison.OrdinalIgnoreCase));
        return taken ? "Another view has that name" : string.Empty;
    }

    // The dialog's controls from a view's settings.
    private void Apply(TaskViewConfig config)
    {
        GroupIndex = Math.Max(0, Array.FindIndex(GroupChoices, choice => choice.Value == config.Group));
        SortIndex = Math.Max(0, Array.FindIndex(ShelfViewModel.SortChoices, choice => choice.Value == config.Sort));
        PinHighlights = config.PinHighlights;
        HighlightedOnly = config.Only.HighlightedOnly;
        MatchAllTags = config.Match == TaskViewConfig.MatchAll;
        var labels = _labels
            .Select(label => new ViewEditorOption(
                label.Id.ToString("D"), label.Name, config.Labels?.Contains(label.Id) == true, TaskCategoryBrushes.Of(TaskLabelColors.Opaque(label.Color))))
            .ToList();
        labels.Add(new ViewEditorOption(NoLabelValue, "No label", config.NoLabel));
        Labels = labels;
        Tags = _tags.Select(tag => new ViewEditorOption(tag, "#" + tag, TaskLabels.Has(config.Tags, tag))).ToList();
        Due = Options(ShelfViewModel.DueChoices, config.Only.Due);
        Time = Options(ShelfViewModel.TimeChoices, config.Only.Time);
        Old = Options(ShelfViewModel.OldChoices, config.Only.Old);
        Icons = BuildIcons();
    }

    // What the dialog shows as a view's settings; what it doesn't show (density, the old status choices, the shuffle order) is kept.
    private TaskViewConfig Config()
    {
        List<Guid> labels = Labels.Where(option => option.IsChecked && option.Value != NoLabelValue).Select(option => Guid.Parse(option.Value)).ToList();
        List<string> tags = Tags.Where(option => option.IsChecked).Select(option => option.Value).ToList();
        string sort = ShelfViewModel.SortChoices[Math.Clamp(SortIndex, 0, ShelfViewModel.SortChoices.Length - 1)].Value;
        return _base with
        {
            Only = new TaskViewFilter(Picked(Due), Picked(Time), Picked(Old), HighlightedOnly),
            Labels = labels.Count == 0 ? null : labels,
            NoLabel = Labels.Any(option => option.IsChecked && option.Value == NoLabelValue),
            Tags = tags.Count == 0 ? null : tags,
            Match = MatchAllTags ? TaskViewConfig.MatchAll : TaskViewConfig.MatchAny,
            Group = GroupChoices[Math.Clamp(GroupIndex, 0, GroupChoices.Length - 1)].Value,
            Sort = sort,
            PinHighlights = PinHighlights,
            Seed = sort == TaskViewConfig.SortShuffle ? _base.Seed ?? Random.Shared.Next(1, int.MaxValue) : _base.Seed,
        };
    }

    private static List<ViewEditorOption> Options((string Value, string Text)[] choices, IReadOnlyList<string>? picked)
    {
        return choices.Select(choice => new ViewEditorOption(choice.Value, choice.Text, picked?.Contains(choice.Value) == true)).ToList();
    }

    private static IReadOnlyList<string>? Picked(IReadOnlyList<ViewEditorOption> options)
    {
        List<string> values = options.Where(option => option.IsChecked).Select(option => option.Value).ToList();
        return values.Count == 0 ? null : values;
    }

    private List<ViewIconChoiceViewModel> BuildIcons()
    {
        return IconNames
            .Select(name => new ViewIconChoiceViewModel(name, ShelfViewModel.ViewIcon(name), name == Icon, new RelayCommand(() => Icon = name)))
            .ToList();
    }
}
