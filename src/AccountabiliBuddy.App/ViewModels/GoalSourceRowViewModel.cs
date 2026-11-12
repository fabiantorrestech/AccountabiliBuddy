using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One row of Edit goal › Tracking &amp; sources › "Counting now" (round 2 R5.7, board Goals-PC-Edit): what the source is, its setting
/// (the label, the #tag, the calendar category, the app, the Beeminder goal and its direction: Read only; Send and Both "Coming soon"
/// until ADR 80), and its switch (counts by itself, brief §5.10). Phone-only sources say "Measured on your phone" (counted there only,
/// until ADR 79). Deep work's one row is locked. <see cref="ToRule"/> gives the stored rule back.
/// </summary>
public sealed partial class GoalSourceRowViewModel : ObservableObject
{
    private readonly GoalSourceRule _rule;
    private readonly Action<GoalSourceRowViewModel> _remove;
    private readonly Action _changed;

    public GoalSourceRowViewModel(GoalSourceRule rule, GoalEditorLookups lookups, bool locked, Action<GoalSourceRowViewModel> remove, Action changed)
    {
        _rule = rule;
        _remove = remove;
        _changed = changed;
        IsLocked = locked;
        Labels = lookups.Labels;
        Categories = lookups.CalendarCategories;
        BeeminderGoals = lookups.BeeminderGoals;
        BeeminderReadText = lookups.BeeminderReadText;
        BeeminderProblemText = lookups.BeeminderProblemText;
        Automatic = rule.Automatic;
        SelectedLabel = Labels.FirstOrDefault(choice => rule.LabelId is Guid id && choice.Key == id.ToString());
        Tag = rule.Tag ?? string.Empty;
        SelectedCategory = Categories.FirstOrDefault(choice => rule.CalendarCategoryId is Guid id && choice.Key == id.ToString());
        App = rule.App ?? string.Empty;
        ScreenAtMost = rule.ScreenDirection != GoalDirection.AtLeast;
        SelectedBeeminder = BeeminderGoals.FirstOrDefault(choice => choice.Key == rule.BeeminderSlug)
            ?? BeeminderGoals.FirstOrDefault(choice => rule.BeeminderSlug is not null && choice.Text == rule.BeeminderSlug)
            ?? (rule.BeeminderSlug is string slug ? new GoalChoice(slug, slug, Note: "Not in the cached list") : null);
        if (SelectedBeeminder is not null && !BeeminderGoals.Contains(SelectedBeeminder))
        {
            BeeminderGoals = [.. BeeminderGoals, SelectedBeeminder];
        }
    }

    public GoalSourceType Type => _rule.Type;

    public Guid Id => _rule.Id;

    /// <summary>Deep work's "all counted focus": no switch, no remove, no other source (plan §3b 6).</summary>
    public bool IsLocked { get; }

    public bool IsEditable => !IsLocked;

    public IReadOnlyList<GoalChoice> Labels { get; }

    public IReadOnlyList<GoalChoice> Categories { get; }

    public IReadOnlyList<GoalChoice> BeeminderGoals { get; }

    /// <summary>"Read 2:05 PM" (ADR 84); null before the first read or when the last one failed.</summary>
    public string? BeeminderReadText { get; }

    /// <summary>"Couldn't reach Beeminder · keeps the last values" after a failed read; null otherwise.</summary>
    public string? BeeminderProblemText { get; }

    public string Title => _rule.Type switch
    {
        GoalSourceType.FocusAll => "All counted focus",
        GoalSourceType.FocusLabel => "Focus time on a label",
        GoalSourceType.FocusTag => "Focus time on a #tag",
        GoalSourceType.Hand => "Tick by hand",
        GoalSourceType.HealthSteps => "Health Connect · steps",
        GoalSourceType.HealthSleep => "Health Connect · sleep",
        GoalSourceType.ScreenTime => "Screen time",
        GoalSourceType.CalendarCategory => "Google Calendar",
        GoalSourceType.Beeminder => SelectedBeeminder is GoalChoice linked ? $"Beeminder · {linked.Key}" : "Beeminder",
        _ => _rule.Type.ToString(),
    };

    public string Detail => _rule.Type switch
    {
        GoalSourceType.FocusAll => "Every counted minute of focus, as the guards count it. Deep work counts nothing else.",
        GoalSourceType.FocusLabel => "Sessions with a task carrying this label.",
        GoalSourceType.FocusTag => "Sessions with a task carrying this #tag. Counted on this PC (the phone shows it as measured on your PC).",
        GoalSourceType.Hand => "Here or on Today.",
        GoalSourceType.HealthSteps => "Measured on your phone (Health Connect, Pixel Watch through it). Daily steps already reach this PC.",
        GoalSourceType.HealthSleep or GoalSourceType.ScreenTime => "Measured on your phone: counted there only for now.",
        GoalSourceType.CalendarCategory => "Time in events of this day plan category. This PC only; it never counts as focus or for a guard.",
        GoalSourceType.Beeminder => SelectedBeeminder?.Note is string note ? $"Counts its daily values on this PC · {note}" : "Counts its daily values on this PC",
        _ => string.Empty,
    };

    public MaterialIconKind Icon => GoalSourceIcons.Of(_rule.Type);

    public bool ShowsLabel => _rule.Type == GoalSourceType.FocusLabel;

    public bool ShowsTag => _rule.Type == GoalSourceType.FocusTag;

    public bool ShowsCategory => _rule.Type == GoalSourceType.CalendarCategory;

    public bool ShowsApp => _rule.Type == GoalSourceType.ScreenTime;

    public bool ShowsBeeminder => _rule.Type == GoalSourceType.Beeminder;

    public bool ShowsSwitch => !IsLocked && !ShowsBeeminder;

    public bool HasNoLabels => ShowsLabel && Labels.Count == 0;

    public bool HasNoCategories => ShowsCategory && Categories.Count == 0;

    /// <summary>Counts by itself (on) or listed only (off).</summary>
    [ObservableProperty]
    public partial bool Automatic { get; set; }

    [ObservableProperty]
    public partial GoalChoice? SelectedLabel { get; set; }

    [ObservableProperty]
    public partial string Tag { get; set; }

    [ObservableProperty]
    public partial GoalChoice? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial string App { get; set; }

    [ObservableProperty]
    public partial bool ScreenAtMost { get; set; }

    [ObservableProperty]
    public partial GoalChoice? SelectedBeeminder { get; set; }

    /// <summary>What still needs a choice before the source counts ("Pick a label"); null when complete.</summary>
    public string? Missing => _rule.Type switch
    {
        GoalSourceType.FocusLabel when SelectedLabel is null => "Pick a label for the focus source.",
        GoalSourceType.FocusTag when string.IsNullOrWhiteSpace(Tag.TrimStart('#')) => "Type the #tag for the focus source.",
        GoalSourceType.CalendarCategory when SelectedCategory is null => "Pick the day plan category for the calendar source.",
        GoalSourceType.ScreenTime when string.IsNullOrWhiteSpace(App) => "Type the app or category for screen time.",
        GoalSourceType.Beeminder when SelectedBeeminder is null => "Pick a Beeminder goal to read.",
        _ => null,
    };

    /// <summary>The rule as it is set now; <paramref name="threshold"/> = a habit's "ticks itself at" for time sources.</summary>
    public GoalSourceRule ToRule(double? threshold)
    {
        if (IsLocked)
        {
            return _rule;
        }

        return _rule with
        {
            Automatic = ShowsBeeminder || Automatic,
            LabelId = ShowsLabel && SelectedLabel is GoalChoice label && Guid.TryParse(label.Key, out Guid labelId) ? labelId : null,
            Tag = ShowsTag ? Tag.Trim().TrimStart('#') : null,
            CalendarCategoryId = ShowsCategory && SelectedCategory is GoalChoice category && Guid.TryParse(category.Key, out Guid categoryId) ? categoryId : null,
            App = ShowsApp ? App.Trim() : null,
            ScreenDirection = ShowsApp ? (ScreenAtMost ? GoalDirection.AtMost : GoalDirection.AtLeast) : null,
            BeeminderSlug = ShowsBeeminder ? SelectedBeeminder?.Key : null,
            BeeminderDirection = ShowsBeeminder ? GoalBeeminderDirection.Read : null,
            ThresholdMinutes = _rule.CountsTime ? threshold : null,
        };
    }

    [RelayCommand]
    private void Remove()
    {
        if (!IsLocked)
        {
            _remove(this);
        }
    }

    partial void OnAutomaticChanged(bool value) => _changed();

    partial void OnSelectedLabelChanged(GoalChoice? value) => Edited();

    partial void OnTagChanged(string value) => Edited();

    partial void OnSelectedCategoryChanged(GoalChoice? value) => Edited();

    partial void OnAppChanged(string value) => Edited();

    partial void OnScreenAtMostChanged(bool value) => _changed();

    partial void OnSelectedBeeminderChanged(GoalChoice? value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Detail));
        Edited();
    }

    private void Edited()
    {
        OnPropertyChanged(nameof(Missing));
        _changed();
    }
}
