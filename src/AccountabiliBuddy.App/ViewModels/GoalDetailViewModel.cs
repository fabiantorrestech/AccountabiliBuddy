using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Goals page's detail pane (round 2, brief §4.4, change note §4a "Goal detail"): the title and its subtitle ("15 min a day ·
/// Learning · daily habit · 6 months going", the age underlined with its tooltip), the tiles by horizon (Today → This period → Target,
/// plus Streak / Total when switched on), What it takes, the heatmap with its day panel, and the bottom cards (Progress tracking,
/// Reminders with the PB badge, Notes). Edit, ✎ and + open R5b's Edit goal at a section (<see cref="EditRequested"/>); ⋮ opens the
/// goal menu (<see cref="MenuRequested"/>).
/// </summary>
public sealed partial class GoalDetailViewModel : ObservableObject
{
    private readonly Func<DeepWorkTargetKind, Task> _cancelPending;
    private readonly Func<Guid, string?> _labelName;
    private Guid? _goalId;
    private GoalFacts? _facts;
    private GoalSourceData? _data;

    private readonly Func<DateTimeOffset> _now;

    public GoalDetailViewModel(
        Func<GoalEntry, Task<GoalChangeResult>> saveEntry, Func<DeepWorkTargetKind, Task> cancelPending, Func<DateTimeOffset> now, Func<Guid, string?> labelName)
    {
        _cancelPending = cancelPending;
        _labelName = labelName;
        _now = now;
        Heatmap = new GoalHeatmapViewModel();
        DayPanel = new GoalDayPanelViewModel(saveEntry, now, labelName);
        WhatItTakes = new GoalWhatItTakesViewModel();
        Heatmap.DayPicked += (_, day) => ShowDay(day);
        DayPanel.BackToTodayRequested += (_, _) => ShowDay(null);
        WhatItTakes.ChangeTargetRequested += (_, _) => RequestEdit(GoalEditSection.Target);
    }

    public GoalHeatmapViewModel Heatmap { get; }

    public GoalDayPanelViewModel DayPanel { get; }

    public GoalWhatItTakesViewModel WhatItTakes { get; }

    public ObservableCollection<GoalTileViewModel> Tiles { get; } = [];

    [ObservableProperty]
    public partial bool HasGoal { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MaterialIconKind Icon { get; set; } = GoalIcons.Goals;

    [ObservableProperty]
    public partial IBrush? ColourBrush { get; set; }

    /// <summary>The subtitle's first part, in ink: "15 min a day".</summary>
    [ObservableProperty]
    public partial string TargetText { get; set; } = string.Empty;

    /// <summary>" · Learning · daily habit · ".</summary>
    [ObservableProperty]
    public partial string MiddleText { get; set; } = string.Empty;

    /// <summary>"6 months going" (dotted underline).</summary>
    [ObservableProperty]
    public partial string AgeText { get; set; } = string.Empty;

    /// <summary>"Since Mon 5/4/2026 · 191 days (27 weeks)".</summary>
    [ObservableProperty]
    public partial string AgeTip { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<GoalSourceLine> ProgressLines { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<GoalReminderRow> Reminders { get; set; } = [];

    [ObservableProperty]
    public partial bool HasReminders { get; set; }

    /// <summary>The PB badge on Reminders (ProductaBuddy mode: suggestions only).</summary>
    [ObservableProperty]
    public partial bool IsProductaBuddy { get; set; }

    [ObservableProperty]
    public partial string RemindersFoot { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NotesPreview { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasNotes { get; set; }

    /// <summary>Deep work: no notes card hook to hand entries, its menu has "Track time only" (R5b).</summary>
    [ObservableProperty]
    public partial bool IsDeepWork { get; set; }

    /// <summary>R5b hook: Edit goal at a section (Edit, the cards' ✎ and +, Change the target).</summary>
    public event EventHandler<GoalEditSection>? EditRequested;

    /// <summary>The ⋮ next to Edit: the goal menu.</summary>
    public event EventHandler? MenuRequested;

    public Guid? GoalId => _goalId;

    /// <summary>Shows a goal's facts (a refresh of the same goal keeps the picked day, the heatmap page and What it takes open).</summary>
    public void Show(
        GoalFacts facts, GoalSourceData data, GoalLists lists, GoalPageSettings settings, DeepWorkGoalSettings deepWork, TimeZoneInfo zone, bool productaBuddy)
    {
        Goal goal = facts.Goal;
        bool sameGoal = _goalId == goal.Id;
        _goalId = goal.Id;
        _facts = facts;
        _data = data;
        HasGoal = true;
        IsDeepWork = goal.IsDeepWork;
        Name = goal.Name;
        Icon = GoalIcons.Of(goal.Icon);
        ColourBrush = GoalIcons.Brush(goal.Color);
        string target = GoalTexts.TargetLine(goal, facts.Today);
        if (goal.IsDeepWork && goal.SecondTarget is { Amount: double weekly })
        {
            target = goal.Target.Amount is null ? $"{GoalTexts.Minutes(weekly)} a week" : $"{target} · {GoalTexts.Minutes(weekly)} a week";
        }

        TargetText = target;
        string area = lists.Areas.FirstOrDefault(item => item.Id == goal.AreaId)?.Name ?? string.Empty;
        string paused = facts.PausedSince is DateOnly since ? $" · paused since {GoalTexts.Date(since, facts.Today)}" : string.Empty;
        MiddleText = $"{(area.Length > 0 ? " · " + area : string.Empty)} · {GoalTexts.KindText(goal)}{paused} · ";
        AgeText = $"{facts.Age.Short} going";
        AgeTip = facts.Age.Tooltip;

        BuildTiles(facts, data, settings, deepWork, zone);
        WhatItTakes.Update(facts.WhatItTakes, goal, facts.Today, sameGoal);
        Heatmap.Show(goal, data, facts.Today, settings.WeekStart, settings.HeatmapLabels, sameGoal);
        if (!sameGoal)
        {
            Heatmap.SelectedDay = null;
        }

        ShowDay(sameGoal ? Heatmap.SelectedDay : null);
        ProgressLines = SourceLines(goal, facts);
        Reminders = ReminderRows(lists.Reminders.Where(item => item.GoalId == goal.Id && item.IsLive).OrderBy(item => item.Position), facts.Today, _now().LocalDateTime);
        HasReminders = Reminders.Count > 0;
        IsProductaBuddy = productaBuddy;
        RemindersFoot = productaBuddy ? "Suggestions only: nothing locks or waits." : "They ring like task reminders. Nothing locks or waits.";
        string notes = goal.Notes?.Trim() ?? string.Empty;
        HasNotes = notes.Length > 0;
        NotesPreview = HasNotes ? (notes.Length > 280 ? notes[..280].TrimEnd() + "…" : notes) : "No notes yet. Why it matters, links: add them in Edit goal › Notes.";
    }

    /// <summary>The day panel goes back to today (the goal menu's Log a value… / Add time by hand…).</summary>
    public void ShowToday() => ShowDay(null);

    /// <summary>Nothing to show (no goals, or goals not ready).</summary>
    public void Clear()
    {
        _goalId = null;
        _facts = null;
        HasGoal = false;
        Tiles.Clear();
    }

    [RelayCommand]
    private void RequestEdit(GoalEditSection section)
    {
        if (_goalId is not null)
        {
            EditRequested?.Invoke(this, section);
        }
    }

    [RelayCommand]
    private void OpenMenu()
    {
        MenuRequested?.Invoke(this, EventArgs.Empty);
    }

    // The day panel shows a picked day, or today.
    private void ShowDay(DateOnly? day)
    {
        if (_facts is not GoalFacts facts || _data is not GoalSourceData data)
        {
            return;
        }

        DateOnly shown = day is DateOnly picked && picked <= facts.Today ? picked : facts.Today;
        Heatmap.SelectedDay = shown == facts.Today ? null : shown;
        GoalDayState state = Heatmap.States.TryGetValue(shown, out GoalDayState? held) ? held : GoalCounting.Day(facts.Goal, data, shown, facts.Today);
        DayPanel.Show(facts.Goal, state, facts.Today);
    }

    private void BuildTiles(GoalFacts facts, GoalSourceData data, GoalPageSettings settings, DeepWorkGoalSettings deepWork, TimeZoneInfo zone)
    {
        Goal goal = facts.Goal;
        GoalExtraTiles extra = goal.ExtraTiles ?? settings.ExtraTiles ?? GoalExtraTiles.None;
        var kinds = new List<string> { "today", "period" };
        if (facts.Target is not null)
        {
            kinds.Add("target");
        }

        if (extra.Streak)
        {
            kinds.Add("streak");
        }

        if (extra.Total)
        {
            kinds.Add("total");
        }

        if (!Tiles.Select(tile => tile.Kind).SequenceEqual(kinds))
        {
            Tiles.Clear();
            foreach (string kind in kinds)
            {
                Tiles.Add(new GoalTileViewModel(kind));
            }
        }

        IBrush colour = GoalIcons.Brush(goal.Color);
        foreach (GoalTileViewModel tile in Tiles)
        {
            tile.ColourBrush = colour;
            tile.Pending = null;
            tile.CancelPendingCommand = null;
            tile.Segments = null;
            tile.ShowsBar = false;
            tile.Mark = double.NaN;
            tile.Status = null;
            tile.IsStatusAmber = false;
            tile.WhatItTakes = tile.IsTarget ? WhatItTakes : null;
            switch (tile.Kind)
            {
                case "today":
                    TodayTile(tile, facts);
                    if (goal.IsDeepWork)
                    {
                        GoalPendingChange? pending = deepWork.PendingTrackTimeOnly ?? deepWork.PendingDaily;
                        DeepWorkTargetKind kind = deepWork.PendingTrackTimeOnly is not null ? DeepWorkTargetKind.TrackTimeOnly : DeepWorkTargetKind.Daily;
                        SetPending(tile, pending, kind, kind == DeepWorkTargetKind.TrackTimeOnly ? "Track time only" : "Lowers to", zone);
                    }

                    break;
                case "period":
                    PeriodTile(tile, facts);
                    if (goal.IsDeepWork && deepWork.PendingTrackTimeOnly is null)
                    {
                        SetPending(tile, deepWork.PendingWeekly, DeepWorkTargetKind.Weekly, "Lowers to", zone);
                    }

                    break;
                case "target":
                    TargetTile(tile, facts);
                    break;
                case "streak":
                    tile.Icon = MaterialIconKind.Fire;
                    tile.Big = facts.Streak.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    tile.Of = null;
                    tile.Caption = goal.Target.Period == GoalPeriod.Day ? "Day streak" : "Streak (periods in a row)";
                    tile.Foot = null;
                    break;
                default:
                {
                    double total = GoalCounting.History(goal, data, facts.Today).Values.Sum(state => goal.Kind == GoalKind.Habit ? (state.Hit ? 1 : 0) : state.Value);
                    tile.Icon = MaterialIconKind.Sigma;
                    tile.Big = goal.Kind == GoalKind.Habit ? GoalTexts.Number(total, 0) : GoalTexts.Value(total, goal.Target.Unit);
                    tile.Of = null;
                    tile.Caption = goal.Kind == GoalKind.Habit ? "Days done in all" : "Total so far";
                    tile.Foot = null;
                    break;
                }
            }

            tile.Changed();
        }
    }

    private static void TodayTile(GoalTileViewModel tile, GoalFacts facts)
    {
        Goal goal = facts.Goal;
        GoalDayState today = facts.TodayState;
        tile.Icon = goal.Kind is GoalKind.Habit or GoalKind.Time ? MaterialIconKind.TimerOutline : GoalIcons.Of(goal.Icon);
        switch (goal.Kind)
        {
            case GoalKind.Habit when facts.Threshold is double threshold:
                tile.Big = ((int)Math.Floor(today.Minutes)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                tile.Of = $"of {GoalTexts.Minutes(threshold)}";
                tile.Caption = "Minutes today";
                tile.ShowsBar = true;
                tile.Fraction = threshold > 0 ? Math.Clamp(today.Minutes / threshold, 0, 1) : 0;
                tile.Foot = today.Hit ? (today.ByHand ? "Done · ticked by hand" : "Done · ticked itself")
                    : $"{GoalTexts.Minutes(Math.Max(0, threshold - today.Minutes))} to go · ticks itself at {GoalTexts.Minutes(threshold)}";
                break;
            case GoalKind.Habit:
                tile.Big = today.Hit ? "Done" : "Not yet";
                tile.Of = null;
                tile.Caption = "Today";
                tile.Foot = today.Hit ? null : "Tick it in the day panel or on Today";
                break;
            case GoalKind.Time:
            {
                tile.Big = GoalTexts.Minutes(today.Value);
                double? daily = goal.Target.Period == GoalPeriod.Day ? goal.Target.Amount : null;
                tile.Of = daily is double amount ? $"of {GoalTexts.Minutes(amount)}" : null;
                tile.Caption = goal.IsDeepWork ? "Focus today" : "Minutes today";
                tile.ShowsBar = daily is not null;
                tile.Fraction = daily is double target && target > 0 ? Math.Clamp(today.Value / target, 0, 1) : 0;
                tile.Foot = daily is double goalMinutes
                    ? (today.Value >= goalMinutes ? "Met today" : $"{GoalTexts.Minutes(goalMinutes - today.Value)} to go")
                    : goal.IsDeepWork && goal.Target.Amount is null ? "Tracking only: no daily target" : null;
                break;
            }

            default:
            {
                tile.Big = today.HasValue ? GoalTexts.Value(today.Value, goal.Target.Unit) : "–";
                double? daily = goal.Target.Period == GoalPeriod.Day && goal.Target.Direction != GoalDirection.Reach ? goal.Target.Amount : null;
                tile.Of = daily is double amount ? $"of {GoalTexts.ShortValue(amount, goal.Target.Unit)}" : null;
                tile.Caption = goal.Kind == GoalKind.Amount && goal.Target.Direction == GoalDirection.Reach ? "Today's reading" : "Today";
                tile.ShowsBar = daily is not null;
                tile.Fraction = daily is double target && target > 0 ? Math.Clamp(today.Value / target, 0, 1) : 0;
                tile.Foot = today.HasValue ? null : "Nothing logged yet today";
                break;
            }
        }
    }

    private static void PeriodTile(GoalTileViewModel tile, GoalFacts facts)
    {
        Goal goal = facts.PeriodGoal;
        GoalPeriodResult period = facts.Period;
        string name = GoalTexts.PeriodName(goal.Target.Period);
        string pace = facts.Status switch
        {
            GoalPeriodStatus.OnTrack => " · on pace",
            GoalPeriodStatus.Behind => " · behind",
            GoalPeriodStatus.NotDueYet => " · not due yet",
            GoalPeriodStatus.Paused => " · paused",
            _ => string.Empty,
        };
        tile.Icon = MaterialIconKind.CalendarCheck;
        double weekTotal = facts.PeriodCells.Count > 0
            ? GoalCountingTotal(facts)
            : period.Progress;
        if (GoalCounting.CountsDays(goal))
        {
            tile.Big = ((int)period.Progress).ToString(System.Globalization.CultureInfo.InvariantCulture);
            tile.Of = $"of {(int)period.Required}";
            bool times = goal.Target.Period != GoalPeriod.Day && goal.Target.WeeklyDays is null && goal.Kind == GoalKind.Habit;
            tile.Caption = $"{(times ? "Times" : "Days")} {name}{pace}";
            tile.Segments = facts.PeriodCells;
            tile.Foot = goal.Kind == GoalKind.Time ? $"{GoalTexts.Minutes(weekTotal)} {name}" : null;
        }
        else if (goal.Target.Amount is double amount && goal.Target.Direction != GoalDirection.Reach)
        {
            tile.Big = GoalTexts.Value(period.Progress, goal.Target.Unit);
            tile.Of = $"of {GoalTexts.ShortValue(amount, goal.Target.Unit)}";
            tile.Caption = $"{char.ToUpperInvariant(name[0])}{name[1..]}{pace}";
            tile.ShowsBar = true;
            tile.Fraction = amount > 0 ? Math.Clamp(period.Progress / amount, 0, 1) : 0;
            tile.Segments = null;
            tile.Foot = null;
        }
        else
        {
            tile.Big = goal.Target.Direction == GoalDirection.Reach ? GoalTexts.Value(period.Progress, goal.Target.Unit) : GoalTexts.Value(weekTotal, goal.Target.Unit);
            tile.Of = null;
            tile.Caption = goal.Target.Direction == GoalDirection.Reach ? $"Latest {name}" : $"{(goal.Target.Unit == GoalUnit.Minutes ? "Time" : "Total")} {name}";
            tile.Segments = facts.PeriodCells;
            tile.Foot = null;
        }
    }

    // This period's total value (the week's minutes for Deep work), from the 30 days held.
    private static double GoalCountingTotal(GoalFacts facts)
    {
        return facts.Recent.Where(state => facts.PeriodSpan.Contains(state.Day)).Sum(state => state.Value);
    }

    private void TargetTile(GoalTileViewModel tile, GoalFacts facts)
    {
        if (facts.Target is not GoalTargetProgress target)
        {
            return;
        }

        Goal goal = facts.Goal;
        GoalUnit unit = goal.Target.Unit;
        tile.Icon = MaterialIconKind.FlagOutline;
        tile.Big = GoalTexts.Value(target.Current, unit);
        tile.Of = $"of {GoalTexts.Value(target.Target, unit)}";
        tile.Caption = goal.Target.TargetDate is DateOnly date ? $"Target · by {GoalTexts.Date(date, facts.Today)}" : "Target";
        tile.ShowsBar = true;
        double from = goal.Target.Direction == GoalDirection.Reach ? target.Start ?? target.Current : 0;
        double span = target.Target - from;
        tile.Fraction = Math.Abs(span) < 1e-9 ? (target.Met ? 1 : 0) : Math.Clamp((target.Current - from) / span, 0, 1);
        tile.Mark = target.ExpectedToday is double expected && Math.Abs(span) > 1e-9 ? Math.Clamp((expected - from) / span, 0, 1) : double.NaN;
        tile.Status = target.Met ? "Met" : target.Status switch
        {
            GoalPeriodStatus.OnTrack => "On track",
            GoalPeriodStatus.Behind => "Behind",
            _ => null,
        };
        tile.IsStatusAmber = !target.Met && target.Status == GoalPeriodStatus.Behind;
        tile.Foot = facts.WhatItTakes?.ProjectedFinish is DateOnly projected ? $"done ~{GoalTexts.Date(projected, facts.Today)}" : null;
    }

    private void SetPending(GoalTileViewModel tile, GoalPendingChange? pending, DeepWorkTargetKind kind, string verb, TimeZoneInfo zone)
    {
        if (pending is null)
        {
            return;
        }

        string what = kind == DeepWorkTargetKind.TrackTimeOnly ? verb
            : pending.Amount is double amount ? $"{verb} {GoalTexts.Minutes(amount)}"
            : "The weekly target goes";
        tile.Pending = $"{what} at {GoalTexts.Time(pending.AppliesAt, zone)}";
        tile.CancelPendingCommand = new AsyncRelayCommand(() => _cancelPending(kind));
    }

    private IReadOnlyList<GoalSourceLine> SourceLines(Goal goal, GoalFacts facts)
    {
        var lines = new List<GoalSourceLine>();
        if (goal.IsDeepWork)
        {
            lines.Add(new GoalSourceLine("All your counted focus, on every device (the guards' rule)"));
            lines.Add(new GoalSourceLine("No hand entries: time added by hand never feeds a guard"));
            return lines;
        }

        // Hand entries always count (the line after the rules says so), so a Hand rule needs no line of its own.
        foreach (GoalSourceRule rule in GoalSources.CountedFor(goal).Where(rule => rule.Type != GoalSourceType.Hand))
        {
            string? label = rule.LabelId is Guid id ? _labelName(id) : null;
            GoalSourceAvailability availability = GoalSources.AvailabilityOf(rule.Type);
            string text = rule.Type switch
            {
                GoalSourceType.FocusAll => "All your counted focus",
                GoalSourceType.FocusLabel => label is null ? "Focus time on tasks with its label" : $"Focus time on tasks labelled {label}",
                GoalSourceType.FocusTag => $"Focus time on tasks tagged #{rule.Tag?.TrimStart('#')}",
                GoalSourceType.CalendarCategory => "Time in a calendar category (this PC)",
                GoalSourceType.HealthSteps => "Daily steps from your phone",
                GoalSourceType.HealthWeight => "Weight from Health Connect",
                GoalSourceType.HealthSleep => "Sleep from Health Connect",
                GoalSourceType.ScreenTime => "Screen time, measured on your phone",
                GoalSourceType.Beeminder => $"Beeminder · {rule.BeeminderSlug}",
                GoalSourceType.GoalField => "Tasks that carry this goal",
                GoalSourceType.MilestoneTasks => "Its milestone tasks",
                _ => "By hand",
            };
            if (!rule.Automatic)
            {
                text += " (off)";
            }

            lines.Add(new GoalSourceLine(availability == GoalSourceAvailability.ComingSoon ? text + " · Coming soon" : text, availability == GoalSourceAvailability.ComingSoon));
        }

        if (facts.Threshold is double threshold)
        {
            lines.Add(new GoalSourceLine($"Ticks itself at {GoalTexts.Minutes(threshold)} in a day"));
        }

        lines.Add(new GoalSourceLine(goal.Kind == GoalKind.Habit ? "Ticking it by hand here or on Today" : "Values and time you add by hand"));
        lines.Add(new GoalSourceLine("Tasks that carry this goal · Coming soon", true));
        return lines;
    }

    // When the reminder rings next, from its day rule and time; null when it never will again (a date or a "today" that has passed).
    public static DateTime? NextRing(GoalReminder reminder, DateTime now)
    {
        DateOnly today = DateOnly.FromDateTime(now);
        DateTime At(DateOnly day) => day.ToDateTime(reminder.Time);
        switch (reminder.Days)
        {
            case GoalReminderDays.EveryDay:
                return At(today) > now ? At(today) : At(today.AddDays(1));
            case GoalReminderDays.Today:
            case GoalReminderDays.Date:
                return reminder.Date is DateOnly date && At(date) > now ? At(date) : null;
            case GoalReminderDays.Weekdays:
                for (int ahead = 0; ahead <= 7; ahead++)
                {
                    DateOnly day = today.AddDays(ahead);
                    if ((reminder.Weekdays ?? []).Any(weekday => (DayOfWeek)weekday == day.DayOfWeek) && At(day) > now)
                    {
                        return At(day);
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private static IReadOnlyList<GoalReminderRow> ReminderRows(IEnumerable<GoalReminder> reminders, DateOnly today, DateTime now)
    {
        reminders = reminders.ToList();
        // The board's "Next" badge: the reminder that rings soonest from now.
        GoalReminder? next = reminders
            .Select(reminder => (Reminder: reminder, At: NextRing(reminder, now)))
            .Where(pair => pair.At is not null)
            .OrderBy(pair => pair.At)
            .Select(pair => pair.Reminder)
            .FirstOrDefault();
        var rows = new List<GoalReminderRow>();
        foreach (GoalReminder reminder in reminders)
        {
            string day = reminder.Days switch
            {
                GoalReminderDays.Today => "Today",
                GoalReminderDays.EveryDay => "Every day",
                GoalReminderDays.Date when reminder.Date is DateOnly date => GoalTexts.Date(date, today),
                GoalReminderDays.Weekdays => string.Join(", ", (reminder.Weekdays ?? []).Select(weekday => ((DayOfWeek)weekday).ToString()[..3])),
                _ => "Once",
            };
            string time = reminder.Time.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
            bool repeats = reminder.Repeat && reminder.Days != GoalReminderDays.Date;
            rows.Add(new GoalReminderRow(rows.Count + 1, day, time, repeats ? MaterialIconKind.Repeat : MaterialIconKind.CalendarCheck,
                repeats ? "Repeats" : "Once", reminder.Reason ?? string.Empty, ReferenceEquals(reminder, next)));
        }

        return rows;
    }
}
