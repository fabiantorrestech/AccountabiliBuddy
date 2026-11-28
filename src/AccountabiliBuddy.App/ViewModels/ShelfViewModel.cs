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
/// the label row and its +N menu (ShelfViewModel.LabelRow.cs), the weekly tidy-up card, the saved views and the View menu (from the page header's
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

    /// <summary>The page bar under the columns (round 2 PG1): which columns show when they do not all fit.</summary>
    public ColumnPagerViewModel Pager { get; } = new();

    /// <summary>The search or a view changed what the Shelf shows: the page builds it again.</summary>
    public event EventHandler? Changed;

    /// <summary>The task list is there: views, Group by and the label chips work.</summary>
    public bool HasViews => _views is not null;

    [ObservableProperty]
    public partial string SearchText { get; set; }

    /// <summary>The words the Shelf is narrowed to; empty for none.</summary>
    public string Search => SearchText.Trim();

    public bool IsSearching => Search.Length > 0;

    /// <summary>"Bins", "Label" or "None": the View menu's Group by value (a view stored as "tag" reads as Bins on the PC).</summary>
    [ObservableProperty]
    public partial string GroupText { get; set; }

    /// <summary>"10 tasks": the Shelf's count (the in-session "The Shelf is hidden" note).</summary>
    [ObservableProperty]
    public partial string CountText { get; set; }

    /// <summary>
    /// Opens the Labels screen (Settings › Tasks › Labels): the +N menu's "Rename, recolour, delete" and the View menu's "Manage labels…".
    /// Set by the app while the task list is the source.
    /// </summary>
    public Action? OpenLabels { get; set; }

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
        _views.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(CustomViewName));
            OnPropertyChanged(nameof(HasCustomView));
            OnPropertyChanged(nameof(ViewsButtonText));
            Changed?.Invoke(this, EventArgs.Empty);
        };
        _tidyUp.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(HasViews));
        OnPropertyChanged(nameof(CustomViewName));
        OnPropertyChanged(nameof(HasCustomView));
        OnPropertyChanged(nameof(ViewsButtonText));
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

        ShowFiltersState();
    }

    /// <summary>What the active view shows of the Shelf's bins, grouped; null before the task list exists.</summary>
    public TaskViewResult? Query() => _views?.Query(Bins, showEmptyBins: true);

    /// <summary>"Uses a label from Laptop. Showing Default.": set by the last <see cref="Query"/> when the view only filters by labels or tags hidden here (ADR 89).</summary>
    public string? FallbackNote => _views?.FallbackNote;

    /// <summary>
    /// The active view's Group by as the PC shows it ("bins", "label", "none"); bins before the task list exists. The PC has no Group by
    /// tag (Tasks redesign, plan conflict #5): a synced view stored as "tag" shows as Bins and keeps its stored value until a choice is picked.
    /// </summary>
    public string Grouping => ShownGroup(_views?.Working.Group);

    /// <summary>The header's Group by toggle: Bins is in force.</summary>
    public bool IsGroupedByBins => Grouping == TaskViewConfig.GroupBins;

    /// <summary>The header's Group by toggle: Labels is in force.</summary>
    public bool IsGroupedByLabels => Grouping == TaskViewConfig.GroupLabel;

    /// <summary>The header's Group by toggle: None (one list) is in force.</summary>
    public bool IsGroupedByNone => Grouping == TaskViewConfig.GroupNone;

    /// <summary>A stored Group by as the PC shows it: "tag" (and anything unknown) reads as "bins".</summary>
    public static string ShownGroup(string? stored)
    {
        return stored is TaskViewConfig.GroupLabel or TaskViewConfig.GroupNone ? stored : TaskViewConfig.GroupBins;
    }

    /// <summary>
    /// The header's Group by toggle (Bins | Labels | None): changes the open view's working copy, as the View menu's Group by does; saving
    /// it stays the View menu's. Picking what already shows changes nothing, so a "tag" view showing as Bins keeps its stored value.
    /// </summary>
    [RelayCommand]
    private void PickGroup(string? group)
    {
        string picked = ShownGroup(group);
        if (_views is null || picked == Grouping)
        {
            return;
        }

        _views.Modify(config => config with { Group = picked });
    }

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

    /// <summary>
    /// The Tasks header's Views button: the name of the view in use when it is not the default one (the button then widens into a tinted
    /// pill with this name); null on the default view or before the task list exists.
    /// </summary>
    public string? CustomViewName =>
        _views?.Active is TaskView active && active.Id != _views.DefaultViewId ? active.Name : null;

    /// <summary>A view other than the default is in use (<see cref="CustomViewName"/>).</summary>
    public bool HasCustomView => CustomViewName is not null;

    /// <summary>The Views button's tooltip and name: "Views", or "Views: Spray - Work" while that view is in use.</summary>
    public string ViewsButtonText => CustomViewName is string name ? "Views: " + name : "Views";

    /// <summary>The Options menu's "Sort ›" value ("Your order", "Newest first", …).</summary>
    public string SortText => SortName(_views?.Working.Sort ?? TaskViewConfig.SortDefault);

    /// <summary>The Options menu's "Sort ›": each order, the one in use ticked.</summary>
    public IReadOnlyList<TaskMenuEntry> SortEntries() => SortMenu(_views?.Working.Sort ?? TaskViewConfig.SortDefault);

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

        return new ViewEditorViewModel(_views, view, LiveLabels(), _store.VisibleTags(), ScopeOf(view?.Config ?? _views.Working));
    }

    /// <summary>The header, chips and card from the task list as it is now (the page calls this as it rebuilds).</summary>
    public void Refresh()
    {
        Services.TickDiag.Count("shelf refresh");
        // A view opened with its own search words: the pill shows them.
        if (_views is not null && _views.Search != SearchText)
        {
            SearchText = _views.Search;
        }

        IsShuffled = _views?.Working.Sort == TaskViewConfig.SortShuffle;
        GroupText = Grouping switch
        {
            TaskViewConfig.GroupLabel => "Label",
            TaskViewConfig.GroupNone => "None",
            _ => "Bins",
        };
        OnPropertyChanged(nameof(IsGroupedByBins));
        OnPropertyChanged(nameof(IsGroupedByLabels));
        OnPropertyChanged(nameof(IsGroupedByNone));
        if (!_labelRowSynced)
        {
            SyncLabelRow();
        }

        _labelRowSynced = false;
        ShowFiltersState();
        OnPropertyChanged(nameof(HasSearchFilters));
        TidyUpCard? card = _tidyUp?.Card();
        HasTidyCard = card is not null;
        TidyTitle = card?.Title ?? string.Empty;
        TidySub = card?.Sub ?? string.Empty;
    }

    /// <summary>The reshuffle button by the Shelf's title: a new shuffled order, kept with the view until the next one.</summary>
    [RelayCommand]
    private Task ReshuffleAsync() => _views?.ReshuffleAsync() ?? Task.CompletedTask;

    /// <summary>
    /// The View menu (spec 03 §C, 05 §10; round 2 R3: Options › Views ›): the saved views (the one in use ticked) with Create a view…,
    /// This view › (Save changes, Edit view…, Make it the default, Delete view), Arrange (Group by ›, Sort › incl. Shuffle, Reshuffle,
    /// Pin highlights), Show only (Labels ›, Tags ›, Due date ›, Time limit ›, Untouched for ›, Highlighted only), and a red Reset (back
    /// to the view as saved). History… went with item 6: its content is in the Archive sheet. Changes apply to the view's working copy; only Save changes stores them. (Hide / Show Today
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
                Value: views.DefaultViewId == id ? "Default" : null, Note: HiddenLabelNote(view.Config)));
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
        bool all = ShownLabelIds(working.Labels).Count == 0 && !working.NoLabel;
        var entries = new List<TaskMenuEntry>
        {
            new("All labels", new RelayCommand(() => _views?.Modify(config => config with { Labels = WithHiddenLabelIds(config.Labels, []), NoLabel = false })), IsChecked: all),
        };
        var items = new List<(bool Shared, TaskMenuEntry Entry)>();
        foreach (TaskLabel label in LiveLabels())
        {
            Guid id = label.Id;
            items.Add((_store!.IsShared(label), new TaskMenuEntry(
                label.Name, new RelayCommand(() => _views?.Modify(config => config with { Labels = Toggled(config.Labels, id) })),
                IsChecked: working.Labels?.Contains(id) == true, Dot: TaskCategoryBrushes.Of(TaskLabelColors.Opaque(label.Color)))));
        }

        entries.AddRange(ScopeMenu.Group(_store!.CanScope, items));
        if (HiddenLabelNote(working) is { } note)
        {
            entries.Add(new TaskMenuEntry(note, Icon: MaterialIconKind.Laptop));
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
        var items = new List<(bool Shared, TaskMenuEntry Entry)>();
        foreach (string tag in _store?.VisibleTags() ?? [])
        {
            string name = tag;
            items.Add((_store!.IsTagShared(name), new TaskMenuEntry(
                "#" + name, new RelayCommand(() => _views?.Modify(config => config with { Tags = ToggledTag(config.Tags, name) })),
                IsChecked: TaskLabels.Has(working.Tags, name))));
        }

        entries.AddRange(ScopeMenu.Group(_store?.CanScope == true, items));

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
        int count = (working.Labels?.Count(id => LiveLabels().Any(label => label.Id == id)) ?? 0) + (working.NoLabel ? 1 : 0);
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

    // ADR 89: the label ids of a view this device shows, and the view's labels with the hidden ones kept (a choice made here never drops a
    // label another device keeps to itself: the view still names it).
    private List<Guid> ShownLabelIds(IReadOnlyList<Guid>? ids)
    {
        HashSet<Guid> live = LiveLabels().Select(label => label.Id).ToHashSet();
        return (ids ?? []).Where(live.Contains).ToList();
    }

    private IReadOnlyList<Guid>? WithHiddenLabelIds(IReadOnlyList<Guid>? current, IEnumerable<Guid> chosen)
    {
        TaskStore? store = _store;
        List<Guid> hidden = (current ?? []).Where(id => store is not null && store.Lists.Labels.Any(item => item.Label.Id == id && item.Label.IsLive && !store.LabelShown(item.Label))).ToList();
        List<Guid> next = hidden.Concat(chosen).Distinct().ToList();
        return next.Count == 0 ? null : next;
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

    // What the view editor needs to know about labels and tags kept to one device (ADR 89).
    private ViewEditorScope ScopeOf(TaskViewConfig config)
    {
        TaskStore store = _store!;
        HashSet<Guid> hidden = (config.Labels ?? []).Where(id => store.Lists.Labels.Any(item => item.Label.Id == id && item.Label.IsLive && !store.LabelShown(item.Label))).ToHashSet();
        return new ViewEditorScope(store.CanScope, store.IsShared, store.IsTagShared, hidden, store.TagShown, HiddenLabelNote(config));
    }

    /// <summary>"Uses a label from Laptop": the view filters by something another device keeps to itself (never its name); null for none.</summary>
    internal string? HiddenLabelNote(TaskViewConfig config)
    {
        TaskStore? store = _store;
        if (store is null)
        {
            return null;
        }

        var labelDevices = new List<string>();
        foreach (Guid id in config.Labels ?? [])
        {
            if (store.Lists.Labels.Select(item => item.Label).FirstOrDefault(label => label.Id == id && label.IsLive && !store.LabelShown(label)) is { } label)
            {
                labelDevices.Add(store.OtherDevicesText(label.OnlyOn) ?? "another device");
            }
        }

        // As the phone says it: a label first, else "Uses a tag from" for a view that filters only by tags.
        if (labelDevices.Count > 0)
        {
            return "Uses a label from " + string.Join(", ", labelDevices.Distinct());
        }

        var tagDevices = new List<string>();
        foreach (string tag in config.Tags ?? [])
        {
            if (!store.TagShown(tag))
            {
                tagDevices.Add(store.OtherDevicesText(TaskScopes.ScopeOf(tag, store.Lists.TagScopes)?.OnlyOn) ?? "another device");
            }
        }

        return tagDevices.Count == 0 ? null : "Uses a tag from " + string.Join(", ", tagDevices.Distinct());
    }

    private List<TaskLabel> LiveLabels()
    {
        return _store is { IsActive: true } store ? store.VisibleLabels.Where(label => label.Name.Trim().Length > 0).ToList() : [];
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

    /// <summary>
    /// The View menu's Group by ›: Bins, Label or None (the view's working copy; saving it is the View menu's, 3.5). No Tag on the PC (plan
    /// conflict #5): a view stored as "tag" ticks Bins.
    /// </summary>
    public IReadOnlyList<TaskMenuEntry> GroupMenu()
    {
        if (_views is null)
        {
            return [new TaskMenuEntry("Bins", IsChecked: true)];
        }

        string current = Grouping;
        return
        [
            GroupEntry("Bins", TaskViewConfig.GroupBins, current),
            GroupEntry("Label", TaskViewConfig.GroupLabel, current),
            GroupEntry("None · one list", TaskViewConfig.GroupNone, current),
        ];
    }

    private TaskMenuEntry GroupEntry(string text, string group, string current)
    {
        MaterialIconKind icon = group switch
        {
            TaskViewConfig.GroupLabel => MaterialIconKind.CircleSlice8,
            TaskViewConfig.GroupNone => MaterialIconKind.FormatListBulleted,
            _ => MaterialIconKind.TrayFull,
        };
        return new TaskMenuEntry(text, new RelayCommand(() => PickGroup(group)), IsChecked: group == current, Icon: icon);
    }
}
