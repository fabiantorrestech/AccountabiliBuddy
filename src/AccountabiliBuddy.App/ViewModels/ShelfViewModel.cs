using System.Windows.Input;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Shelf of the Tasks page (spec 03 §B, shared behaviour §1; round 2 R3: the page itself, its bins as columns): the search pill,
/// the label chips (+, All, each label, No label), the weekly tidy-up card, the saved views and the View menu (from the page header's
/// Views and Options), then its sections (<see cref="Panel"/>: To sort, Next, Later, or label / tag groups, each drawn as a column).
/// What it shows comes from the active view (<see cref="TaskViewService"/>); its rows are built by <see cref="TasksViewModel"/>, which
/// owns the lists. Before the task list exists (the old lists still the source) only the search narrows it and grouping stays by bins.
/// </summary>
public sealed partial class ShelfViewModel : ObservableObject
{
    /// <summary>The Shelf's bins, in order.</summary>
    public static readonly IReadOnlyList<TaskBin> Bins = [TaskBin.Sort, TaskBin.Next, TaskBin.Later];

    private TaskViewService? _views;
    private TaskTidyUpService? _tidyUp;
    private TaskStore? _store;
    private TaskActionsService? _actions;
    private TimeProvider _time = TimeProvider.System;

    public ShelfViewModel(TaskSectionsPanelViewModel panel)
    {
        Panel = panel;
        SearchText = CountText = TidyTitle = TidySub = string.Empty;
        GroupText = "Bins";
    }

    public TaskSectionsPanelViewModel Panel { get; }

    /// <summary>The search or a view changed what the Shelf shows: the page builds it again.</summary>
    public event EventHandler? Changed;

    /// <summary>The task list is there: views, Group by and the label chips work.</summary>
    public bool HasViews => _views is not null;

    [ObservableProperty]
    public partial string SearchText { get; set; }

    /// <summary>The words the Shelf is narrowed to; empty for none.</summary>
    public string Search => SearchText.Trim();

    public bool IsSearching => Search.Length > 0;

    [ObservableProperty]
    public partial IReadOnlyList<TaskChipViewModel> LabelChips { get; set; } = [];

    /// <summary>"Bins", "Label", "Tag" or "None": the Group by button's text.</summary>
    [ObservableProperty]
    public partial string GroupText { get; set; }

    /// <summary>"10 tasks": the Shelf's count (the in-session "The Shelf is hidden" note).</summary>
    [ObservableProperty]
    public partial string CountText { get; set; }

    /// <summary>
    /// Opens the Labels screen (Settings › Tasks › Labels): the chip row's "+" and the View menu's "Manage labels…". Set by the app while
    /// the task list is the source; without it the "+" opens the older editor under the chips.
    /// </summary>
    public Action? OpenLabels { get; set; }

    /// <summary>The labels editor (the chip row's "+") is open under the chips (only without the Labels screen).</summary>
    [ObservableProperty]
    public partial bool IsManagingLabels { get; set; }

    [ObservableProperty]
    public partial bool HasTidyCard { get; set; }

    /// <summary>"5 tasks untouched for 3+ weeks".</summary>
    [ObservableProperty]
    public partial string TidyTitle { get; set; }

    /// <summary>"About 2 minutes · Mondays".</summary>
    [ObservableProperty]
    public partial string TidySub { get; set; }

    /// <summary>"Shuffle" sorts the Shelf: the reshuffle button shows by its title.</summary>
    [ObservableProperty]
    public partial bool IsShuffled { get; set; }

    /// <summary>
    /// "Create a view…" (null) or "Edit view…" (that view) was chosen: the page opens the Create / Edit view dialog (<see cref="ViewEditor"/>),
    /// which saves through <see cref="TaskViewService.CreateAsync"/> / <see cref="TaskViewService.EditAsync"/>. The entries show only while
    /// something listens.
    /// </summary>
    public event EventHandler<TaskView?>? ViewEditorRequested;

    /// <summary>"Save it as a view" (the session ended notice): opens Create a view from what the Shelf shows now; false when nothing listens.</summary>
    public bool RequestCreateView()
    {
        if (ViewEditorRequested is null)
        {
            return false;
        }

        ViewEditorRequested.Invoke(this, null);
        return true;
    }

    /// <summary>From now on the Shelf follows the task list's views and its weekly tidy-up (ADR 76); History acts through the task actions.</summary>
    public void UseTaskModel(TaskStore store, TaskViewService views, TaskTidyUpService tidyUp, TaskActionsService actions, TimeProvider time)
    {
        _store = store;
        _views = views;
        _tidyUp = tidyUp;
        _actions = actions;
        _time = time;
        _views.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _tidyUp.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(HasViews));
        OnPropertyChanged(nameof(HasHistory));
        StartTidyCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));
        if (_views is null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else if (_views.Search != value)
        {
            _views.SetSearch(value);
        }
    }

    /// <summary>What the active view shows of the Shelf's bins, grouped; null before the task list exists.</summary>
    public TaskViewResult? Query() => _views?.Query(Bins, showEmptyBins: true);

    /// <summary>The active view's Group by ("bins", "label", "tag", "none"); bins before the task list exists.</summary>
    public string Grouping => _views?.Working.Group ?? TaskViewConfig.GroupBins;

    /// <summary>The Options menu's "Views ›" value: "★ Default" for the default view in use, else the view's name.</summary>
    public string? ActiveViewText
    {
        get
        {
            if (_views?.Active is not TaskView active)
            {
                return null;
            }

            return _views.DefaultViewId == active.Id ? "★ " + active.Name : active.Name;
        }
    }

    /// <summary>The Options menu's "Sort ›" value ("Your order", "Newest first", …).</summary>
    public string SortText => SortName(_views?.Working.Sort ?? TaskViewConfig.SortDefault);

    /// <summary>The Options menu's "Sort ›": each order, the one in use ticked.</summary>
    public IReadOnlyList<TaskMenuEntry> SortEntries() => SortMenu(_views?.Working.Sort ?? TaskViewConfig.SortDefault);

    [RelayCommand]
    private void ToggleManageLabels()
    {
        if (OpenLabels is not null)
        {
            OpenLabels();
            return;
        }

        IsManagingLabels = !IsManagingLabels;
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    /// <summary>The tidy-up card's ×: not this week.</summary>
    [RelayCommand]
    private Task DismissTidyAsync() => _tidyUp?.DismissAsync() ?? Task.CompletedTask;

    /// <summary>
    /// The tidy-up card's "Tidy up": begins the tidy-up over the untouched tasks and asks the page for its dialog
    /// (<see cref="TidyUpRequested"/>). Never during a session (the service refuses, and the card is not shown then).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartTidy))]
    private void StartTidy()
    {
        if (TidyUpRequested is null || _tidyUp is null || _store is null || _tidyUp.Start() is null)
        {
            return;
        }

        TidyUpRequested.Invoke(this, new TidyUpViewModel(_tidyUp, _store, _time));
    }

    private bool CanStartTidy() => _tidyUp is not null;

    /// <summary>"Tidy up" was clicked and the tidy-up began: the page shows its flow (the tidy-up dialog).</summary>
    public event EventHandler<TidyUpViewModel>? TidyUpRequested;

    /// <summary>The Create a view (null) / Edit view dialog's data; null before the task list exists.</summary>
    public ViewEditorViewModel? ViewEditor(TaskView? view)
    {
        if (_views is null || _store is null)
        {
            return null;
        }

        IEnumerable<IReadOnlyList<string>?> tagLists = _store.Lists.Tasks.Where(item => item.Task.IsLive).Select(item => item.Task.Tags);
        return new ViewEditorViewModel(_views, view, LiveLabels(), TaskLabels.AllOf(tagLists).ToList());
    }

    /// <summary>The header, chips and card from the task list as it is now (the page calls this as it rebuilds).</summary>
    public void Refresh()
    {
        // A view opened with its own search words: the pill shows them.
        if (_views is not null && _views.Search != SearchText)
        {
            SearchText = _views.Search;
        }

        IsShuffled = _views?.Working.Sort == TaskViewConfig.SortShuffle;
        GroupText = (_views?.Working.Group ?? TaskViewConfig.GroupBins) switch
        {
            TaskViewConfig.GroupLabel => "Label",
            TaskViewConfig.GroupTag => "Tag",
            TaskViewConfig.GroupNone => "None",
            _ => "Bins",
        };
        LabelChips = BuildChips();
        TidyUpCard? card = _tidyUp?.Card();
        HasTidyCard = card is not null;
        TidyTitle = card?.Title ?? string.Empty;
        TidySub = card?.Sub ?? string.Empty;
    }

    // "All", each live label (its dot), "No label"; one picked at a time (the View menu, 3.5, does several).
    private List<TaskChipViewModel> BuildChips()
    {
        if (_views is null || _store is null)
        {
            return [];
        }

        TaskViewConfig working = _views.Working;
        bool noRestriction = working.Labels is not { Count: > 0 } && !working.NoLabel;
        var chips = new List<TaskChipViewModel> { new("All", noRestriction, () => PickLabel(null, false)) };
        foreach (TaskLabel label in _store.Lists.Labels.Select(item => item.Label).Where(label => label.IsLive && label.Name.Trim().Length > 0)
                     .OrderBy(label => label.Position))
        {
            Guid id = label.Id;
            bool only = working.Labels is { Count: 1 } labels && labels[0] == id && !working.NoLabel;
            chips.Add(new TaskChipViewModel(label.Name, only, () => PickLabel(id, false), TaskCategoryBrushes.Of(TaskLabelColors.Opaque(label.Color))));
        }

        bool onlyNone = working.NoLabel && working.Labels is not { Count: > 0 };
        chips.Add(new TaskChipViewModel("No label", onlyNone, () => PickLabel(null, true)));
        return chips;
    }

    private Task PickLabel(Guid? id, bool noLabel)
    {
        _views?.Modify(config => config with { Labels = id is Guid one ? [one] : null, NoLabel = noLabel });
        return Task.CompletedTask;
    }

    /// <summary>History (the View menu's "History…"): a fresh list of the last 30 days; null before the task list exists.</summary>
    public TaskHistoryViewModel? History() => _actions is null ? null : new TaskHistoryViewModel(_actions, _time, HistoryLabel);

    // A History row's label: the task list's live label with that id.
    private AccountabiliBuddy.Core.Domain.TaskLabel? HistoryLabel(Guid? labelId)
    {
        return labelId is Guid id ? _store?.Lists.Labels.Select(item => item.Label).FirstOrDefault(label => label.IsLive && label.Id == id) : null;
    }

    /// <summary>The reshuffle button by the Shelf's title: a new shuffled order, kept with the view until the next one.</summary>
    [RelayCommand]
    private Task ReshuffleAsync() => _views?.ReshuffleAsync() ?? Task.CompletedTask;

    /// <summary>
    /// The View menu (spec 03 §C, 05 §10; round 2 R3: Options › Views ›): the saved views (the one in use ticked) with Create a view…,
    /// This view › (Save changes, Edit view…, Make it the default, Delete view), Arrange (Group by ›, Sort › incl. Shuffle, Reshuffle,
    /// Pin highlights), Show only (Labels ›, Tags ›, Due date ›, Time limit ›, Untouched for ›, Highlighted only), History…, and a red
    /// Reset (back to the view as saved). Changes apply to the view's working copy; only Save changes stores them. (Hide / Show Today
    /// went with the Tasks page's Today column.)
    /// </summary>
    public IReadOnlyList<TaskMenuEntry> ViewMenu()
    {
        var entries = new List<TaskMenuEntry>();
        if (_views is TaskViewService views)
        {
            TaskViewConfig working = views.Working;
            entries.AddRange(SavedViewsMenu());
            entries.Add(TaskMenuEntry.Separator);
            entries.Add(new TaskMenuEntry("Arrange", IsHeading: true));
            entries.Add(new TaskMenuEntry("Group by", Children: GroupMenu(), Icon: MaterialIconKind.FormatListGroup, Value: GroupText));
            entries.Add(new TaskMenuEntry("Sort", Children: SortMenu(working.Sort), Icon: MaterialIconKind.Sort, Value: SortName(working.Sort)));
            if (working.Sort == TaskViewConfig.SortShuffle)
            {
                entries.Add(TaskMenuEntry.Async("Reshuffle", views.ReshuffleAsync, MaterialIconKind.ShuffleVariant));
            }

            entries.Add(new TaskMenuEntry(
                "Pin highlights to the top", new RelayCommand(() => views.Modify(config => config with { PinHighlights = !config.PinHighlights })),
                Icon: working.PinHighlights ? MaterialIconKind.CheckboxMarked : MaterialIconKind.CheckboxBlankOutline));

            entries.Add(TaskMenuEntry.Separator);
            entries.Add(new TaskMenuEntry("Show only", IsHeading: true));
            entries.Add(new TaskMenuEntry("Labels", Children: LabelFilterMenu(working), Icon: MaterialIconKind.CircleSlice8, Value: LabelFilterText(working)));
            entries.Add(new TaskMenuEntry("Tags", Children: TagFilterMenu(working), Icon: MaterialIconKind.Pound, Value: PickedText(working.Tags?.Count)));
            entries.Add(new TaskMenuEntry(
                "Due date", Children: PickMenu(DueChoices, working.Only.Due, list => working.Only with { Due = list }),
                Icon: MaterialIconKind.CalendarBlankOutline, Value: PickedText(working.Only.Due?.Count)));
            entries.Add(new TaskMenuEntry(
                "Time limit", Children: PickMenu(TimeChoices, working.Only.Time, list => working.Only with { Time = list }),
                Icon: MaterialIconKind.TimerOutline, Value: PickedText(working.Only.Time?.Count)));
            entries.Add(new TaskMenuEntry(
                "Untouched for", Children: PickMenu(OldChoices, working.Only.Old, list => working.Only with { Old = list }),
                Icon: MaterialIconKind.Sleep, Value: PickedText(working.Only.Old?.Count)));
            entries.Add(new TaskMenuEntry(
                "Highlighted only",
                new RelayCommand(() => views.Modify(config => config with { Only = config.Only with { HighlightedOnly = !config.Only.HighlightedOnly } })),
                Icon: working.Only.HighlightedOnly ? MaterialIconKind.CheckboxMarked : MaterialIconKind.CheckboxBlankOutline));

            entries.Add(TaskMenuEntry.Separator);
            entries.Add(new TaskMenuEntry("History…", ViewAction: "history", Icon: MaterialIconKind.History, Value: "30 days"));
            entries.Add(new TaskMenuEntry(
                "Reset", views.IsChanged ? new RelayCommand(() => views.Select(views.ActiveId)) : null, Icon: MaterialIconKind.Restore, IsDanger: views.IsChanged));
        }

        return entries;
    }

    /// <summary>
    /// The saved views (the one in use ticked, the default marked), Create a view… and This view ›: the top of the View menu, and the
    /// whole menu of the Tasks header's Views (bookmark) button (round 2, brief §3.3). Empty before the task list exists.
    /// </summary>
    public IReadOnlyList<TaskMenuEntry> SavedViewsMenu()
    {
        if (_views is not TaskViewService views)
        {
            return [];
        }

        var entries = new List<TaskMenuEntry> { new("Saved views", IsHeading: true) };
        foreach (TaskView view in views.Views)
        {
            Guid id = view.Id;
            entries.Add(new TaskMenuEntry(
                view.Name, new RelayCommand(() => views.Select(id)), IsChecked: id == views.ActiveId, Icon: ViewIcon(view.Icon),
                Value: views.DefaultViewId == id ? "Default" : null));
        }

        if (ViewEditorRequested is not null)
        {
            entries.Add(TaskMenuEntry.Do("Create a view…", () => ViewEditorRequested?.Invoke(this, null), MaterialIconKind.BookmarkPlusOutline));
        }

        if (views.Active is TaskView active)
        {
            entries.Add(TaskMenuEntry.Group("This view", ThisViewMenu(views, active), MaterialIconKind.BookmarkOutline));
        }

        return entries;
    }

    /// <summary>History is there (the task list's actions): the Tasks header's History button shows.</summary>
    public bool HasHistory => _actions is not null;

    // "Show only" choices: the stored value and what the menu says.
    internal static readonly (string Value, string Text)[] DueChoices =
        [("overdue", "Overdue"), ("today", "Today"), ("tomorrow", "Tomorrow"), ("thisWeek", "This week"), ("none", "No due date")];

    internal static readonly (string Value, string Text)[] TimeChoices =
        [("le10", "10 min or less"), ("le25", "25 min or less"), ("le50", "50 min or less"), ("ge50", "50 min or more"), ("none", "No time limit")];

    internal static readonly (string Value, string Text)[] OldChoices =
        [("1w", "1 week or more"), ("2w", "2 weeks or more"), ("4w", "4 weeks or more"), ("8w", "8 weeks or more")];

    internal static readonly (string Value, string Text, MaterialIconKind Icon)[] SortChoices =
    [
        (TaskViewConfig.SortDefault, "Your order", MaterialIconKind.Drag),
        (TaskViewConfig.SortNewest, "Newest first", MaterialIconKind.SortClockDescendingOutline),
        (TaskViewConfig.SortOldest, "Oldest first", MaterialIconKind.SortClockAscendingOutline),
        (TaskViewConfig.SortDueAscending, "Due date: soonest first", MaterialIconKind.SortCalendarAscending),
        (TaskViewConfig.SortDueDescending, "Due date: latest first", MaterialIconKind.SortCalendarDescending),
        (TaskViewConfig.SortUntouched, "Untouched longest first", MaterialIconKind.Sleep),
        (TaskViewConfig.SortLongest, "Time: long to short", MaterialIconKind.SortNumericDescending),
        (TaskViewConfig.SortShortest, "Time: short to long", MaterialIconKind.SortNumericAscending),
        (TaskViewConfig.SortShuffle, "Shuffle", MaterialIconKind.ShuffleVariant),
    ];

    // Save changes (faded until something changed), Edit view… (the dialog), Make it the default, Delete view (not a built-in one;
    // asks once in a submenu).
    private List<TaskMenuEntry> ThisViewMenu(TaskViewService views, TaskView active)
    {
        var entries = new List<TaskMenuEntry>
        {
            new("Save changes", views.IsChanged ? new AsyncRelayCommand(views.SaveAsync) : null, Icon: MaterialIconKind.ContentSaveOutline),
        };
        if (ViewEditorRequested is not null)
        {
            entries.Add(TaskMenuEntry.Do("Edit view…", () => ViewEditorRequested?.Invoke(this, active), MaterialIconKind.PencilOutline));
        }

        Guid id = active.Id;
        bool isDefault = views.DefaultViewId == id;
        entries.Add(new TaskMenuEntry(
            "Make it the default", isDefault ? null : new AsyncRelayCommand(() => views.SetDefaultAsync(id == TaskView.DefaultId ? null : id)),
            IsChecked: isDefault, Icon: MaterialIconKind.StarOutline));
        if (!active.BuiltIn)
        {
            entries.Add(new TaskMenuEntry(
                "Delete view",
                Children:
                [
                    new TaskMenuEntry($"Delete “{active.Name}”", new AsyncRelayCommand(() => views.DeleteAsync(id)), Icon: MaterialIconKind.DeleteOutline, IsDanger: true),
                ],
                Icon: MaterialIconKind.DeleteOutline,
                IsDanger: true));
        }

        return entries;
    }

    // Sort: each order, the one in use ticked. Shuffle stores a new order with the view (synced) until the next reshuffle.
    private List<TaskMenuEntry> SortMenu(string current)
    {
        var entries = new List<TaskMenuEntry>();
        foreach ((string value, string text, MaterialIconKind icon) in SortChoices)
        {
            string sort = value;
            ICommand command = sort == TaskViewConfig.SortShuffle
                ? new AsyncRelayCommand(() => _views?.ReshuffleAsync() ?? Task.CompletedTask)
                : new RelayCommand(() => _views?.Modify(config => config with { Sort = sort }));
            entries.Add(new TaskMenuEntry(text, command, IsChecked: sort == current, Icon: icon));
        }

        return entries;
    }

    // Labels: All, each label (ticks widen: any of them matches), No label.
    private List<TaskMenuEntry> LabelFilterMenu(TaskViewConfig working)
    {
        bool all = working.Labels is not { Count: > 0 } && !working.NoLabel;
        var entries = new List<TaskMenuEntry>
        {
            new("All labels", new RelayCommand(() => _views?.Modify(config => config with { Labels = null, NoLabel = false })), IsChecked: all),
        };
        foreach (TaskLabel label in LiveLabels())
        {
            Guid id = label.Id;
            entries.Add(new TaskMenuEntry(
                label.Name, new RelayCommand(() => _views?.Modify(config => config with { Labels = Toggled(config.Labels, id) })),
                IsChecked: working.Labels?.Contains(id) == true, Dot: TaskCategoryBrushes.Of(TaskLabelColors.Opaque(label.Color))));
        }

        entries.Add(new TaskMenuEntry(
            "No label", new RelayCommand(() => _views?.Modify(config => config with { NoLabel = !config.NoLabel })), IsChecked: working.NoLabel));
        if (OpenLabels is not null)
        {
            entries.Add(TaskMenuEntry.Separator);
            entries.Add(TaskMenuEntry.Do("Manage labels…", OpenLabels, MaterialIconKind.CogOutline));
        }

        return entries;
    }

    // Tags: Any tag, each tag, and "Has every ticked tag" (Match all instead of any).
    private List<TaskMenuEntry> TagFilterMenu(TaskViewConfig working)
    {
        var entries = new List<TaskMenuEntry>
        {
            new("Any tag", new RelayCommand(() => _views?.Modify(config => config with { Tags = null })), IsChecked: working.Tags is not { Count: > 0 }),
        };
        IEnumerable<IReadOnlyList<string>?> tagLists = _store?.Lists.Tasks.Where(item => item.Task.IsLive).Select(item => item.Task.Tags) ?? [];
        foreach (string tag in TaskLabels.AllOf(tagLists))
        {
            string name = tag;
            entries.Add(new TaskMenuEntry(
                "#" + name, new RelayCommand(() => _views?.Modify(config => config with { Tags = ToggledTag(config.Tags, name) })),
                IsChecked: TaskLabels.Has(working.Tags, name)));
        }

        entries.Add(TaskMenuEntry.Separator);
        bool matchAll = working.Match == TaskViewConfig.MatchAll;
        entries.Add(new TaskMenuEntry(
            "Has every ticked tag",
            new RelayCommand(() => _views?.Modify(config => config with { Match = matchAll ? TaskViewConfig.MatchAny : TaskViewConfig.MatchAll })),
            Icon: matchAll ? MaterialIconKind.CheckboxMarked : MaterialIconKind.CheckboxBlankOutline));
        return entries;
    }

    // A "Show only" list: each choice ticks on or off (ticks in one list widen it), then "Clear this filter".
    private List<TaskMenuEntry> PickMenu((string Value, string Text)[] choices, IReadOnlyList<string>? current, Func<IReadOnlyList<string>?, TaskViewFilter> with)
    {
        List<TaskMenuEntry> entries = choices
            .Select(choice => new TaskMenuEntry(
                choice.Text, new RelayCommand(() => _views?.Modify(config => config with { Only = with(Toggled(current, choice.Value)) })),
                IsChecked: current?.Contains(choice.Value) == true))
            .ToList();
        entries.Add(TaskMenuEntry.Separator);
        entries.Add(new TaskMenuEntry(
            "Clear this filter", current is { Count: > 0 } ? new RelayCommand(() => _views?.Modify(config => config with { Only = with(null) })) : null));
        return entries;
    }

    private string LabelFilterText(TaskViewConfig working)
    {
        int count = (working.Labels?.Count ?? 0) + (working.NoLabel ? 1 : 0);
        if (count == 0)
        {
            return "All";
        }

        if (count == 1 && working.Labels is [Guid one])
        {
            return LiveLabels().FirstOrDefault(label => label.Id == one)?.Name ?? "1 picked";
        }

        return count == 1 ? "No label" : count + " picked";
    }

    private static string? PickedText(int? count) => count is > 0 ? count + " picked" : null;

    private static string SortName(string sort)
    {
        foreach ((string value, string text, _) in SortChoices)
        {
            if (value == sort)
            {
                return text;
            }
        }

        return "Your order";
    }

    private static IReadOnlyList<T>? Toggled<T>(IReadOnlyList<T>? list, T value)
    {
        List<T> items = (list ?? []).ToList();
        if (!items.Remove(value))
        {
            items.Add(value);
        }

        return items.Count == 0 ? null : items;
    }

    // Tags compare without case (TaskLabels).
    private static IReadOnlyList<string>? ToggledTag(IReadOnlyList<string>? tags, string tag)
    {
        return TaskLabels.Has(tags, tag) ? TaskLabels.Without(tags, tag) : TaskLabels.With(tags, [tag]);
    }

    private List<TaskLabel> LiveLabels()
    {
        return _store?.Lists.Labels.Select(item => item.Label).Where(label => label.IsLive && label.Name.Trim().Length > 0).OrderBy(label => label.Position).ToList() ?? [];
    }

    // A view's icon name ("lightning-bolt") as a Material icon; a bookmark when it has none or one this app doesn't know.
    internal static MaterialIconKind ViewIcon(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return MaterialIconKind.BookmarkOutline;
        }

        string pascal = string.Concat(name.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        return Enum.TryParse(pascal, out MaterialIconKind kind) ? kind : MaterialIconKind.BookmarkOutline;
    }

    /// <summary>"Bins ▾": Group by Bins, Label, Tag or None (the view's working copy; saving it is the View menu's, 3.5).</summary>
    public IReadOnlyList<TaskMenuEntry> GroupMenu()
    {
        if (_views is null)
        {
            return [new TaskMenuEntry("Bins", IsChecked: true)];
        }

        string current = _views.Working.Group;
        return
        [
            GroupEntry("Bins", TaskViewConfig.GroupBins, current),
            GroupEntry("Label", TaskViewConfig.GroupLabel, current),
            GroupEntry("Tag", TaskViewConfig.GroupTag, current),
            GroupEntry("None · one list", TaskViewConfig.GroupNone, current),
        ];
    }

    private TaskMenuEntry GroupEntry(string text, string group, string current)
    {
        MaterialIconKind icon = group switch
        {
            TaskViewConfig.GroupLabel => MaterialIconKind.CircleSlice8,
            TaskViewConfig.GroupTag => MaterialIconKind.Pound,
            TaskViewConfig.GroupNone => MaterialIconKind.FormatListBulleted,
            _ => MaterialIconKind.TrayFull,
        };
        return new TaskMenuEntry(text, new RelayCommand(() => _views?.Modify(config => config with { Group = group })), IsChecked: group == current, Icon: icon);
    }
}
