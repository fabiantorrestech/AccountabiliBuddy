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

    /// <summary>The weekly review's timing and answers (set by the app; the This week card's review row reads it).</summary>
    public GoalReviewService? Review { get; set; }

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

    /// <summary>The presets "Start a session ›" offers (set by the app: this PC's focus profiles, the Quick Start default first).</summary>
    public Func<IReadOnlyList<GoalSessionPreset>>? SessionPresets { get; set; }

    /// <summary>Starts a preset through the usual session start (its rules apply); returns why not, or null (set by the app).</summary>
    public Func<Guid, Task<string?>>? StartPreset { get; set; }

    /// <summary>"Custom…": the usual start controls (set by the app).</summary>
    public Action? OpenStartControls { get; set; }

    /// <summary>A session runs now (Start a session is greyed).</summary>
    public Func<bool>? SessionRunning { get; set; }

    /// <summary>Opens an address in the user's own browser (Open in Beeminder; set by the app).</summary>
    public Action<string>? OpenInBrowser { get; set; }

    /// <summary>A question to ask before something that can't be undone or that waits (Delete goal…, Track time only).</summary>
    public event EventHandler<GoalConfirmRequest>? ConfirmRequested;

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
        List.ThisWeek.ReviewNote = GoalThisWeekViewModel.ReviewText(settings.ReviewDay, settings.ReviewSettings.TidyUpAfter);
        if (Review is not null)
        {
            List.ThisWeek.SetReview(Review.Row());
        }
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

    /// <summary>Back / forward (owner 2026-11-14): the goal is still there (false once deleted).</summary>
    public bool HasGoal(Guid id) => _goals.Store.Lists.LiveGoals.Any(goal => goal.Id == id);

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
    /// The goal menu (right-click, the Menu key, Shift+F10, the row's ⋯, the detail's ⋮; brief §4.4, change note §4a; no shortcut letters):
    /// the goal's name; Start a session › (the presets inline: the default ✓, the others, Custom…; the usual session start, never around
    /// its rules) │ the kind's quick log (Tick today / Log a value… / Add time by hand… / Open its tasks (Coming soon, plan §3b 2) / Open
    /// in Beeminder when linked) │ Edit goal…, Reminders…, Move to area ›, Show on Today ✓, Pause goal… (opens the Active switch: the
    /// only place that pauses) │ Delete goal… (red), or for Deep work "Track time only, no target" (through its waiting rule).
    /// </summary>
    public IReadOnlyList<TaskMenuEntry> GoalMenu(Guid id)
    {
        if (_goals.Store.Lists.Goals.FirstOrDefault(goal => goal.Id == id && goal.IsLive) is not Goal stored)
        {
            return [];
        }

        Goal goal = stored.IsDeepWork && _goals.DeepWork is Goal view ? view : stored;
        DateOnly today = _data.Today;
        bool paused = goal.IsPausedOn(today);
        var entries = new List<TaskMenuEntry> { new(goal.Name, IsHeading: true) };

        entries.Add(StartSessionEntry(goal));
        entries.Add(TaskMenuEntry.Separator);
        entries.AddRange(QuickLogEntries(goal, today, paused));
        entries.Add(TaskMenuEntry.Separator);

        int reminders = _goals.Lists.Reminders.Count(reminder => reminder.GoalId == id && reminder.IsLive);
        entries.Add(new TaskMenuEntry("Edit goal…", new RelayCommand(() => RequestEdit(id, GoalEditSection.Basics)), Icon: MaterialIconKind.PencilOutline,
            Note: "Target, tracking & sources, notes", OpensMore: true));
        entries.Add(new TaskMenuEntry("Reminders…", new RelayCommand(() => RequestEdit(id, GoalEditSection.Reminders)), Icon: MaterialIconKind.BellOutline,
            Value: reminders == 0 ? "None" : $"{reminders} set", OpensMore: true));
        entries.Add(MoveToAreaEntry(stored));
        entries.Add(new TaskMenuEntry("Show on Today", new AsyncRelayCommand(() => ToggleShowOnTodayAsync(id)), IsChecked: goal.ShowOnToday,
            Icon: MaterialIconKind.PinOutline, Note: "On Today's goals card"));
        if (goal.IsDeepWork)
        {
            entries.Add(new TaskMenuEntry("Pause goal", Icon: MaterialIconKind.Pause, Note: "Deep work can't be paused"));
        }
        else
        {
            entries.Add(new TaskMenuEntry(paused ? "Resume goal…" : "Pause goal…", new RelayCommand(() => RequestEdit(id, GoalEditSection.Basics, focusActive: true)),
                Icon: paused ? MaterialIconKind.Play : MaterialIconKind.Pause,
                Note: paused ? "Turn Active back on: counts from today" : "Stops counting and reminders · keeps history"));
        }

        entries.Add(TaskMenuEntry.Separator);
        if (goal.IsDeepWork)
        {
            entries.Add(_goals.DeepWorkSettings.TrackTimeOnly
                ? new TaskMenuEntry("Set a target again…", new RelayCommand(() => RequestEdit(id, GoalEditSection.Target)), Icon: MaterialIconKind.FlagOutline,
                    Note: "Raising applies at once", OpensMore: true)
                : new TaskMenuEntry("Track time only, no target", new RelayCommand(() => ConfirmRequested?.Invoke(this, GoalConfirmPrompts.TrackTimeOnly(_goals))),
                    Icon: MaterialIconKind.TimerOutline, Note: "Keeps counting focus · lowering waits"));
        }
        else
        {
            entries.Add(new TaskMenuEntry("Delete goal…", new RelayCommand(() => ConfirmRequested?.Invoke(this, GoalConfirmPrompts.DeleteGoal(_goals, id))),
                Icon: MaterialIconKind.DeleteOutline, IsDanger: true));
        }

        return entries;
    }

    /// <summary>The goal menu's Tick today / Untick today (a habit; a hand entry of 1 or 0 for today, as the day panel's Tick it).</summary>
    public async Task<GoalChangeResult> TickTodayAsync(Guid id)
    {
        if (_goals.Store.Lists.Goals.FirstOrDefault(goal => goal.Id == id && goal.IsLive) is not Goal goal || goal.IsDeepWork || goal.Kind != GoalKind.Habit)
        {
            return GoalChangeResult.NotAllowed;
        }

        DateOnly today = _data.Today;
        bool hit = GoalCounting.Day(goal, _data.Current, today, today).Hit;
        var entry = new GoalEntry(Guid.NewGuid(), id, today, null, hit ? 0 : 1, GoalEntrySource.Hand, _data.Now);
        return await _goals.SaveEntryAsync(entry, this);
    }

    /// <summary>The goal menu's Log a value… / Add time by hand…: the goal is shown with today's day panel open on Edit.</summary>
    public void LogToday(Guid id)
    {
        Select(id);
        Detail.ShowToday();
        if (Detail.DayPanel is { CanEdit: true, IsEditing: false } panel)
        {
            panel.EditCommand.Execute(null);
        }
    }

    /// <summary>The goal menu's Move to area › (at once: areas feed no guard).</summary>
    public async Task<GoalChangeResult> MoveToAreaAsync(Guid goalId, Guid areaId)
    {
        if (_goals.Store.Lists.Goals.FirstOrDefault(goal => goal.Id == goalId && goal.IsLive) is not Goal goal || goal.AreaId == areaId)
        {
            return GoalChangeResult.Unchanged;
        }

        double position = _goals.Lists.LiveGoals.Where(item => item.AreaId == areaId).Select(item => item.Position).DefaultIfEmpty(0).Max() + 1;
        return await _goals.UpdateGoalAsync(goal with { AreaId = areaId, Position = position }, this);
    }

    private TaskMenuEntry StartSessionEntry(Goal goal)
    {
        if (SessionRunning?.Invoke() == true)
        {
            return new TaskMenuEntry("Start a session", Icon: MaterialIconKind.PlayOutline, Note: "A session is running");
        }

        IReadOnlyList<GoalSessionPreset> presets = SessionPresets?.Invoke() ?? [];
        var children = new List<TaskMenuEntry> { new($"Counts toward {goal.Name}", IsHeading: true) };
        foreach (GoalSessionPreset preset in presets.OrderByDescending(item => item.IsDefault))
        {
            Guid profileId = preset.ProfileId;
            children.Add(new TaskMenuEntry(preset.Name, new AsyncRelayCommand(() => StartPresetAsync(profileId)), IsChecked: preset.IsDefault,
                Value: preset.IsDefault ? "Default" : null));
        }

        if (presets.Count > 0)
        {
            children.Add(TaskMenuEntry.Separator);
        }

        children.Add(new TaskMenuEntry("Custom…", OpenStartControls is null ? null : new RelayCommand(() => OpenStartControls()), OpensMore: true));
        string? value = presets.FirstOrDefault(item => item.IsDefault)?.Name;
        return new TaskMenuEntry("Start a session", Children: children, Icon: MaterialIconKind.PlayOutline, Value: value);
    }

    private IEnumerable<TaskMenuEntry> QuickLogEntries(Goal goal, DateOnly today, bool paused)
    {
        Guid id = goal.Id;
        if (goal.IsDeepWork)
        {
            yield return new TaskMenuEntry("Add time by hand…", Icon: MaterialIconKind.TimerOutline, Note: "Deep work counts focus only: add a session in History");
        }
        else if (paused)
        {
            yield return new TaskMenuEntry(goal.Kind == GoalKind.Habit ? "Tick today" : "Log today", Icon: MaterialIconKind.Check, Note: "Paused: nothing to log");
        }
        else
        {
            switch (goal.Kind)
            {
                case GoalKind.Habit:
                {
                    GoalDayState state = GoalCounting.Day(goal, _data.Current, today, today);
                    string? soFar = GoalTexts.Threshold(goal) is double threshold
                        ? $"{GoalTexts.Minutes(state.Minutes)} of {GoalTexts.Minutes(threshold)} so far"
                        : null;
                    yield return new TaskMenuEntry(state.Hit ? "Untick today" : "Tick today", new AsyncRelayCommand(() => TickTodayAsync(id)),
                        Icon: MaterialIconKind.Check, Note: soFar);
                    break;
                }

                case GoalKind.Amount:
                    yield return new TaskMenuEntry("Log a value…", new RelayCommand(() => LogToday(id)), Icon: MaterialIconKind.PencilOutline, OpensMore: true);
                    break;
                case GoalKind.Time:
                    yield return new TaskMenuEntry("Add time by hand…", new RelayCommand(() => LogToday(id)), Icon: MaterialIconKind.TimerOutline,
                        Note: "Counts for the goal, never for a guard", OpensMore: true);
                    break;
                default:
                    yield return new TaskMenuEntry("Open its tasks", Icon: MaterialIconKind.FormatListChecks, Note: "Coming soon");
                    break;
            }
        }

        if (goal.Sources.FirstOrDefault(rule => rule.Type == GoalSourceType.Beeminder && rule.BeeminderSlug is not null) is { BeeminderSlug: string slug })
        {
            yield return new TaskMenuEntry("Open in Beeminder", OpenInBrowser is null ? null : new RelayCommand(() => OpenInBrowser(BeeminderSite)),
                Icon: MaterialIconKind.OpenInNew, Value: slug);
        }
    }

    private TaskMenuEntry MoveToAreaEntry(Goal goal)
    {
        List<GoalArea> areas = _goals.Lists.Areas.Where(area => area.IsLive).OrderBy(area => area.Position).ToList();
        List<TaskMenuEntry> children = areas.Select(area =>
        {
            Guid areaId = area.Id;
            return new TaskMenuEntry(area.Name, new AsyncRelayCommand(() => MoveToAreaAsync(goal.Id, areaId)), IsChecked: area.Id == goal.AreaId, Icon: GoalIcons.Of(area.Icon));
        }).ToList();
        children.Add(TaskMenuEntry.Separator);
        children.Add(new TaskMenuEntry("Areas…", new RelayCommand(() => AreasRequested?.Invoke(this, EventArgs.Empty)), OpensMore: true));
        string? current = areas.FirstOrDefault(area => area.Id == goal.AreaId)?.Name;
        return new TaskMenuEntry("Move to area", Children: children, Icon: MaterialIconKind.SwapHorizontal, Value: current);
    }

    private async Task StartPresetAsync(Guid profileId)
    {
        if (StartPreset is not null && await StartPreset(profileId) is string why)
        {
            // Said in the page's small dialog (nothing to run): the start's own rules held it.
            ConfirmRequested?.Invoke(this, new GoalConfirmRequest("The session didn't start", why, "Start", IsDanger: false, CanRun: false, () => Task.FromResult<string?>(null)));
        }
    }

    private void RequestEdit(Guid id, GoalEditSection section, bool focusActive = false)
    {
        EditGoalRequested?.Invoke(this, new GoalEditRequest(id, section, focusActive));
    }

    // Beeminder's site, opened in the user's own browser (the app itself connects nowhere for it; ADR 43 unchanged). The cached list keeps
    // no user name, so a goal's own page can't be addressed: the site's front page opens the signed-in user's dashboard (Q430).
    private const string BeeminderSite = "https://www.beeminder.com/";

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
