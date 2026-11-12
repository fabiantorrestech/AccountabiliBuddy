using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The PC's Goals page (round 2 R5.1–R5.3; brief §4.4, change note §4, §4a): the header "Goals · Thu 11/12" with + New goal, Stats
/// and Options (last), the list pane (<see cref="List"/>) and the detail pane (<see cref="Detail"/>) with one splitter between them
/// (list 360 to 560 px, default 470, remembered in "goals.ui"). Everything shown is computed from <see cref="GoalsService"/> and the
/// Core goals maths over <see cref="GoalSourceDataService"/>'s data; nothing here feeds a guard. Dialogs are R5b's: New goal, Edit
/// goal (at a section), Edit area / Areas, the weekly review, Settings › Goals and the full goal menu are hooks (events and
/// actions set by the app). Use from the UI thread only.
/// </summary>
public sealed partial class GoalsViewModel : ObservableObject
{
    private readonly GoalsService _goals;
    private readonly GoalSourceDataService _data;
    private readonly Func<bool> _isProductaBuddy;
    private IReadOnlyList<GoalFacts> _facts = [];
    private bool _dirty = true;

    public GoalsViewModel(GoalsService goals, GoalSourceDataService data, Func<bool> isProductaBuddy, Func<Guid, string?>? labelName = null)
    {
        _goals = goals;
        _data = data;
        _isProductaBuddy = isProductaBuddy;
        Func<Guid, string?> labels = labelName ?? (_ => null);
        List = new GoalListViewModel(row => Select(row.Id), SetViewAsync, ToggleAreaAsync, JumpToBehind);
        Detail = new GoalDetailViewModel(entry => _goals.SaveEntryAsync(entry), kind => _goals.CancelDeepWorkPendingAsync(kind), () => _data.Now, labels);
        Detail.EditRequested += (_, section) =>
        {
            if (Detail.GoalId is Guid id)
            {
                EditGoalRequested?.Invoke(this, new GoalEditRequest(id, section));
            }
        };
        Detail.MenuRequested += (_, _) => DetailMenuRequested?.Invoke(this, EventArgs.Empty);
        List.EditAreaRequested += (_, id) => EditAreaRequested?.Invoke(this, id);
        List.ThisWeek.StartReviewRequested += (_, _) => ReviewRequested?.Invoke(this, EventArgs.Empty);
        List.ThisWeek.SnoozeReviewRequested += (_, _) => ReviewSnoozeRequested?.Invoke(this, EventArgs.Empty);
        ListWidth = goals.Store.Ui.ListWidth;
        _data.Changed += (_, _) => MarkChanged();
    }

    public GoalListViewModel List { get; }

    public GoalDetailViewModel Detail { get; }

    /// <summary>"Thu 11/12".</summary>
    [ObservableProperty]
    public partial string DateText { get; set; } = string.Empty;

    /// <summary>The list pane's width as stored (360 to 560); the page may show it narrower in a small window.</summary>
    [ObservableProperty]
    public partial double ListWidth { get; set; }

    /// <summary>The goals can be read and changed on this PC (false: the store isn't ready or can't be read).</summary>
    [ObservableProperty]
    public partial bool IsReady { get; set; }

    [ObservableProperty]
    public partial string? NotReadyText { get; set; }

    [ObservableProperty]
    public partial Guid? SelectedId { get; set; }

    /// <summary>The page is on screen: changes are drawn at once (off screen they wait for the next visit).</summary>
    public bool IsShown { get; private set; }

    /// <summary>The facts of the last refresh (list order), for the checks and the Today sidebar's twin.</summary>
    public IReadOnlyList<GoalFacts> Facts => _facts;

    public static MaterialIconKind StatsIcon => MaterialIconKind.ChartTimelineVariant;

    /// <summary>R5b hook: + New goal.</summary>
    public event EventHandler? NewGoalRequested;

    /// <summary>R5b hook: Edit goal at a section (Edit, ✎ / + on the cards, Change the target, the goal menu's Edit goal…).</summary>
    public event EventHandler<GoalEditRequest>? EditGoalRequested;

    /// <summary>R5b hook: Edit area (✎ on an area header).</summary>
    public event EventHandler<Guid>? EditAreaRequested;

    /// <summary>R5b hook: Options › Areas….</summary>
    public event EventHandler? AreasRequested;

    /// <summary>R5b hook: the This week card's Start (the weekly goals review dialog).</summary>
    public event EventHandler? ReviewRequested;

    /// <summary>R5b hook: the This week card's Snooze.</summary>
    public event EventHandler? ReviewSnoozeRequested;

    /// <summary>The detail's ⋮ (the view opens the goal menu under it).</summary>
    public event EventHandler? DetailMenuRequested;

    /// <summary>A row should come into view (the This week card's "N behind" jump).</summary>
    public event EventHandler<Guid>? BringIntoViewRequested;

    /// <summary>Stats, for the goal shown (set by the app; Stats can't filter by goal yet, so it opens Stats).</summary>
    public Action<Guid?>? OpenStats { get; set; }

    /// <summary>R5b hook: Options' last row, Settings › Goals (set by the app).</summary>
    public Action? OpenGoalsSettings { get; set; }

    /// <summary>The Goals tab was shown or left (the app calls this on every tab change).</summary>
    public void SetShown(bool shown)
    {
        IsShown = shown;
        if (shown && _dirty)
        {
            Refresh();
        }
    }

    /// <summary>Something the page shows changed: redraw now when on screen, else on the next visit.</summary>
    public void MarkChanged()
    {
        _dirty = true;
        if (IsShown)
        {
            Refresh();
        }
    }

    /// <summary>Recomputes every goal's facts for today and updates the list and the detail in place.</summary>
    public void Refresh()
    {
        _dirty = false;
        DateOnly today = _data.Today;
        DateText = GoalTexts.Date(today, today);
        GoalStore store = _goals.Store;
        IsReady = store.IsReady;
        if (!store.IsReady)
        {
            NotReadyText = _goals.State == GoalModelState.Unreadable
                ? "This PC's goals can't be read right now. Nothing was changed; a backup restore can bring them back."
                : "Goals aren't set up on this PC yet. They will be after the next start.";
            _facts = [];
            Detail.Clear();
            return;
        }

        NotReadyText = null;
        GoalPageSettings settings = store.PageSettings;
        GoalSourceData data = _data.Current;
        Goal? deepWork = _goals.DeepWork;
        List<Goal> goals = store.Lists.LiveGoals.Select(goal => goal.IsDeepWork && deepWork is not null ? deepWork : goal).ToList();
        _facts = goals.Select(goal => GoalFactsBuilder.Build(goal, data, today, settings.WeekStart)).ToList();
        if (Math.Abs(ListWidth - store.Ui.ListWidth) > 0.5)
        {
            ListWidth = store.Ui.ListWidth;
        }

        List.Update(_facts, store.Lists, store.Ui, today, settings.WeekStart);
        Guid? selected = SelectedId is Guid id && _facts.Any(item => item.Goal.Id == id) ? id : List.ShownRows.FirstOrDefault()?.Id ?? _facts.FirstOrDefault()?.Goal.Id;
        SelectedId = selected;
        foreach (GoalRowViewModel row in List.Rows.Values)
        {
            row.IsSelected = row.Id == selected;
        }

        if (_facts.FirstOrDefault(item => item.Goal.Id == selected) is GoalFacts shown)
        {
            Detail.Show(shown, data, store.Lists, settings, _goals.DeepWorkSettings, _data.Zone, _isProductaBuddy());
        }
        else
        {
            Detail.Clear();
        }
    }

    /// <summary>Shows a goal in the detail pane (clicking a row never moves it).</summary>
    public void Select(Guid id)
    {
        if (SelectedId == id)
        {
            return;
        }

        SelectedId = id;
        Refresh();
    }

    /// <summary>The splitter was let go, a key moved it or a double-click reset it: remember the width (360 to 560).</summary>
    public Task CommitListWidthAsync(double width)
    {
        int clamped = (int)Math.Round(Math.Clamp(width, GoalUiPreferences.MinListWidth, GoalUiPreferences.MaxListWidth));
        ListWidth = clamped;
        return _goals.SetUiPreferencesAsync(_goals.Store.Ui with { ListWidth = clamped }, this);
    }

    /// <summary>The Options (tune) menu: List, Charts, then Goals settings › (last).</summary>
    public IReadOnlyList<TaskMenuEntry> OptionsMenu()
    {
        GoalStore store = _goals.Store;
        GoalPageSettings settings = store.PageSettings;
        return PageOptionsMenus.Goals(
            store.Ui.ListView, SetViewAsync,
            settings.WeekStart, weekStart => _goals.SetPageSettingsAsync(settings with { WeekStart = weekStart }, this),
            settings.HeatmapLabels, labels => _goals.SetPageSettingsAsync(settings with { HeatmapLabels = labels }, this),
            settings.ExtraTiles ?? GoalExtraTiles.None, tiles => _goals.SetPageSettingsAsync(settings with { ExtraTiles = tiles }, this),
            () => AreasRequested?.Invoke(this, EventArgs.Empty),
            () => OpenGoalsSettings?.Invoke());
    }

    /// <summary>
    /// The goal menu (right-click, the Menu key, Shift+F10, the row's ⋯, the detail's ⋮). R5b builds the full menu (Start a session ›,
    /// the kind's quick log, Reminders…, Move to area ›, Pause goal, Delete goal… / Track time only); until then: the goal's name,
    /// Edit goal…, Show on Today ✓ (it already works: Today's goals read it), and a note.
    /// </summary>
    public IReadOnlyList<TaskMenuEntry> GoalMenu(Guid id)
    {
        if (_goals.Store.Lists.Goals.FirstOrDefault(goal => goal.Id == id && goal.IsLive) is not Goal goal)
        {
            return [];
        }

        return
        [
            new TaskMenuEntry(goal.Name, IsHeading: true),
            new TaskMenuEntry("Edit goal…", new RelayCommand(() => EditGoalRequested?.Invoke(this, new GoalEditRequest(id, GoalEditSection.Basics))), Icon: MaterialIconKind.PencilOutline, OpensMore: true),
            new TaskMenuEntry("Show on Today", new AsyncRelayCommand(() => ToggleShowOnTodayAsync(id)), IsChecked: goal.ShowOnToday, Icon: MaterialIconKind.PinOutline),
            TaskMenuEntry.Separator,
            new TaskMenuEntry("More in the next build", IsHeading: true, Note: "Start a session, quick log, reminders, move, pause"),
        ];
    }

    /// <summary>Show on Today ✓ (applies at once: display only).</summary>
    public async Task ToggleShowOnTodayAsync(Guid id)
    {
        if (_goals.Store.Lists.Goals.FirstOrDefault(goal => goal.Id == id && goal.IsLive) is Goal goal)
        {
            await _goals.UpdateGoalAsync(goal with { ShowOnToday = !goal.ShowOnToday }, this);
        }
    }

    [RelayCommand]
    private void NewGoal()
    {
        NewGoalRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Stats()
    {
        OpenStats?.Invoke(SelectedId);
    }

    private Task SetViewAsync(GoalListView view)
    {
        return _goals.SetUiPreferencesAsync(_goals.Store.Ui with { ListView = view }, this);
    }

    private Task ToggleAreaAsync(Guid areaId)
    {
        GoalUiPreferences ui = _goals.Store.Ui;
        List<Guid> folded = (ui.CollapsedAreas ?? []).ToList();
        if (!folded.Remove(areaId))
        {
            folded.Add(areaId);
        }

        return _goals.SetUiPreferencesAsync(ui with { CollapsedAreas = folded }, this);
    }

    // The This week card's "N behind": the first goal behind is shown and scrolled to (the pill itself stays where it is).
    private void JumpToBehind()
    {
        GoalRowViewModel? target = List.ShownRows.FirstOrDefault(item => item.IsBehind) ?? List.Rows.Values.FirstOrDefault(item => item.IsBehind);
        if (target is null)
        {
            return;
        }

        List.IsGoalsOpen = true;
        if (List.Blocks.FirstOrDefault(block => block.Rows.Contains(target)) is { IsFolded: true, AreaId: Guid folded })
        {
            _ = ToggleAreaAsync(folded);
        }
        else if (!List.ShownRows.Contains(target))
        {
            _ = SetViewAsync(GoalListView.All);
        }

        Select(target.Id);
        BringIntoViewRequested?.Invoke(this, target.Id);
    }
}
