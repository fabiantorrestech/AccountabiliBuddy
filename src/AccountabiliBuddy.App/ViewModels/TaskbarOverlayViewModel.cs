using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// What the taskbar overlay says (ADR 51, boards 13a and 14a): the session's minutes and task, a ring or timeline, Pause and Done, this
/// PC's clock and batteries and the sound, in whichever state the day is in (focus, wrapping up, break, planning, away, drift, meeting soon,
/// work done, wind down, or nothing running). It only reads: the session, the checklist, the plan and the enforcement presenter's drift
/// notice. Its commands go through the same objects as the Today page's buttons, so no gate is bypassed. Use from the UI thread only.
/// </summary>
public sealed partial class TaskbarOverlayViewModel : ObservableObject
{
    private static readonly TimeSpan WrapUpLeft = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MeetingSoonWithin = TimeSpan.FromMinutes(10);

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly OverlaySettingsService _settings;
    private readonly IBatteryReader _battery;
    private readonly IBatteryVisibility _visibility;

    private (string Label, DateTimeOffset Since)? _drift;

    public TaskbarOverlayViewModel(
        SessionController sessions, ConfigurationService configuration, OverlaySettingsService settings, DayBarViewModel day, IBatteryReader battery,
        IBatteryVisibility visibility, OverlayActions actions)
    {
        _sessions = sessions;
        _configuration = configuration;
        _settings = settings;
        _battery = battery;
        _visibility = visibility;
        Day = day;
        Actions = actions;
        MinutesText = TaskText = Line2 = ClockText = DateText = TrackText = string.Empty;
        BorderBrush = OverlayBrushes.Get("Line");
        RingBrush = OverlayBrushes.Get("Accent");
        MinutesBrush = OverlayBrushes.Get("Ink");
        DotBrush = OverlayBrushes.Get("Drift");
    }

    public DayBarViewModel Day { get; }

    public OverlayActions Actions { get; }

    /// <summary>The running session's checklist (set by the app).</summary>
    public ChecklistViewModel? Checklist { get; set; }

    public StepAwayService? StepAway { get; set; }

    public PlanFirstService? PlanFirst { get; set; }

    public NoisePlayerViewModel? Noise { get; set; }

    public OverlayTimersViewModel? Timers { get; set; }

    public TonightWakeViewModel? Wake { get; set; }

    /// <summary>Today's deep work against the goal ("Goal · 40m of 1h").</summary>
    public string GoalText { get; set; } = string.Empty;

    /// <summary>The panel's Hide: out of the way for a few seconds (set by the service).</summary>
    public Action? HideRequested { get; set; }

    /// <summary>After a session ended: "Session done", shown for a few seconds when nothing else would show (set by the service).</summary>
    public string? DoneNotice { get; set; }

    /// <summary>The overlay lights up for a moment (time is up, a timer or alarm rang): its border goes ink until then.</summary>
    public DateTimeOffset GlowUntil { get; set; }

    public OverlaySettings Settings => _settings.Current;

    [ObservableProperty]
    public partial OverlayState State { get; set; }

    /// <summary>"18 min", "Nothing running", "Break · 7 min": the first, bold line.</summary>
    [ObservableProperty]
    public partial string MinutesText { get; set; }

    /// <summary>The task, or what the state adds to the minutes ("to plan", "reddit.com for 1 min").</summary>
    [ObservableProperty]
    public partial string TaskText { get; set; }

    /// <summary>The vertical tile (8d) splits the minutes: "18" over "min".</summary>
    public string MinutesNumber => MinutesText.Split(' ')[0];

    public string MinutesUnit => MinutesText.Contains(' ') ? MinutesText[(MinutesText.IndexOf(' ') + 1)..] : string.Empty;

    partial void OnMinutesTextChanged(string value)
    {
        OnPropertyChanged(nameof(MinutesNumber));
        OnPropertyChanged(nameof(MinutesUnit));
    }

    /// <summary>The second, muted line: "ends 2:20 PM · break next".</summary>
    [ObservableProperty]
    public partial string Line2 { get; set; }

    [ObservableProperty]
    public partial double RingValue { get; set; }

    [ObservableProperty]
    public partial IBrush RingBrush { get; set; }

    [ObservableProperty]
    public partial IBrush MinutesBrush { get; set; }

    [ObservableProperty]
    public partial IBrush BorderBrush { get; set; }

    [ObservableProperty]
    public partial bool ShowsDot { get; set; }

    [ObservableProperty]
    public partial IBrush DotBrush { get; set; }

    [ObservableProperty]
    public partial double TimelineDone { get; set; }

    [ObservableProperty]
    public partial double TimelineLeft { get; set; }

    [ObservableProperty]
    public partial double TimelineBreak { get; set; }

    [ObservableProperty]
    public partial double TimelineNext { get; set; }

    [ObservableProperty]
    public partial bool IsSession { get; set; }

    [ObservableProperty]
    public partial bool ShowsPause { get; set; }

    [ObservableProperty]
    public partial MaterialIconKind PauseIcon { get; set; } = MaterialIconKind.Pause;

    [ObservableProperty]
    public partial bool ShowsDone { get; set; }

    [ObservableProperty]
    public partial bool ShowsStart { get; set; }

    [ObservableProperty]
    public partial string ClockText { get; set; }

    [ObservableProperty]
    public partial string DateText { get; set; }

    [ObservableProperty]
    public partial string TrackText { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial bool HasChangeWaiting { get; set; }

    /// <summary>The settings say to draw a ring (not the timeline) and the state has an arc to draw.</summary>
    [ObservableProperty]
    public partial bool ShowsRing { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowsTask { get; set; } = true;

    /// <summary>The timeline sits under the text (the Progress setting is Timeline).</summary>
    [ObservableProperty]
    public partial bool ShowsInlineTimeline { get; set; }

    /// <summary>Drift replaces the ring with a dot.</summary>
    [ObservableProperty]
    public partial bool ShowsDriftDot { get; set; }

    /// <summary>The drift dot sits before the text (there is no ring slot to put it in).</summary>
    [ObservableProperty]
    public partial bool ShowsInlineDot { get; set; }

    /// <summary>The tile is the one-line Small size: the second line is dropped.</summary>
    [ObservableProperty]
    public partial bool IsSmall { get; set; }

    /// <summary>What the settings and the size let the tile show; set by <see cref="ApplyLayout"/> when they change.</summary>
    public void ApplyLayout(OverlaySettings settings)
    {
        bool ringItem = settings.Items.Any(item => item.Kind == OverlayItemKind.Ring && item.Enabled);
        ShowsRing = ringItem && settings.Progress == OverlayProgress.Ring;
        ShowsTask = settings.Items.Any(item => item.Kind == OverlayItemKind.Task && item.Enabled);
        ShowsInlineTimeline = settings.Progress == OverlayProgress.Timeline && ringItem;
        IsSmall = settings.Size == OverlaySize.Small;
        ShowsInlineDot = ShowsDriftDot && !ShowsRing;
    }

    [RelayCommand]
    private void OpenTrayOverflow()
    {
        Actions.OpenTrayOverflow();
    }

    [RelayCommand]
    private void OpenCalendar()
    {
        Actions.OpenCalendarFlyout();
    }

    /// <summary>The batteries the collapsed overlay shows (this PC's own; others only when their sync data carries one).</summary>
    public ObservableCollection<OverlayBatteryRow> Batteries { get; } = [];

    /// <summary>True when the overlay shows the PC's clock (it covers the tray, or the Wide size).</summary>
    public bool HasBattery => Batteries.Count > 0;

    /// <summary>The ring's fraction of the session still to go, 0 to 1 (full when nothing counts).</summary>
    public double Fraction { get; private set; }

    /// <summary>The countdown, as a time span (for the panel's end text); zero when none.</summary>
    public TimeSpan Left { get; private set; }

    /// <summary>"ends 2:20 PM" or "since 1:05 PM" (the panel's header).</summary>
    public string EndsText { get; private set; } = string.Empty;

    public string ProfileName { get; private set; } = string.Empty;

    /// <summary>The drift notice step 1 is showing: what the user drifted to (a site or an app) and since when; null when none.</summary>
    public void SetDrift(string? label, DateTimeOffset since)
    {
        _drift = label is null ? null : (label, since);
    }

    /// <summary>The nudge dot (18d): a card was shown and went unread; the overlay keeps a dot until the user looks.</summary>
    [ObservableProperty]
    public partial bool HasNudgeDot { get; set; }

    [RelayCommand]
    private void Pause()
    {
        StepAway?.Toggle();
    }

    [RelayCommand]
    private async Task DoneAsync()
    {
        if (Checklist is { HasTasks: true, CurrentTaskNumber: > 0 } checklist)
        {
            checklist.TickCurrentTaskCommand.Execute(null);
            return;
        }

        await Actions.EndSession();
    }

    [RelayCommand]
    private void Start()
    {
        Actions.ShowStart();
    }

    [RelayCommand]
    private void ExtendFocus()
    {
        Actions.ExtendFocus();
    }

    /// <summary>Re-reads everything the overlay says. Cheap: called at each session tick, and when something it reads changes.</summary>
    public void Refresh()
    {
        OverlaySettings settings = _settings.Current;
        SessionState session = _sessions.State;
        DateTimeOffset now = _sessions.Now;
        DateTime local = TimeZoneInfo.ConvertTime(now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        ClockText = OverlayTimeText.Clock(local);
        DateText = OverlayTimeText.Date(local);
        RefreshBatteries();
        RefreshSound();
        IsSession = session.IsActive;
        ProfileName = session.IsTest ? "Test session" : ProfileOptions.NameOf(_configuration.Current.Profiles, session.ProfileId) ?? "Focus";
        DateTimeOffset? changeAt = Actions.PendingChangeAt();
        HasChangeWaiting = changeAt is not null;
        string waiting = changeAt is DateTimeOffset at ? $"Change waiting · applies {TimeZoneInfo.ConvertTime(at, zone).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}" : string.Empty;

        string accent = "Accent";
        string border = "Line";
        string minutesInk = "Ink";
        bool dot = false;
        double timelineLeft = 0;
        OverlayState state;

        if (session.IsActive)
        {
            DateTimeOffset clock = session.CountdownTime(now);
            DateTimeOffset start = session.PhaseStartedAt ?? clock;
            DateTimeOffset? end = session.PhaseEndsAt;
            bool hasEnd = end is DateTimeOffset e && e > start;
            TimeSpan left = end is DateTimeOffset until ? until - clock : TimeSpan.Zero;
            Left = left;
            Fraction = hasEnd ? Math.Clamp((end!.Value - clock) / (end.Value - start), 0, 1) : 0;
            string minutes = hasEnd ? OverlayTimeText.Left(left, settings.ShowSeconds) : OverlayTimeText.Left(clock - start, settings.ShowSeconds).Replace("less than 1 min", "0 min");
            string endsAt = end is DateTimeOffset finish ? TimeZoneInfo.ConvertTime(finish, zone).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
            EndsText = end is null ? $"since {TimeZoneInfo.ConvertTime(start, zone).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}" : $"ends {endsAt}";
            string task = Checklist is { HasTasks: true } && Checklist.CurrentTaskText.Length > 0 ? Checklist.CurrentTaskText : ProfileName;
            string progress = Checklist is { HasTasks: true, ProgressText.Length: > 0 } ? $" · {Checklist.ProgressText.Replace("/", " of ")}" : string.Empty;
            string next = session.Mode == SessionMode.Pomodoro && session.Phase == SessionPhase.Focus ? " · break next" : string.Empty;

            if (_drift is { } drift && session.IsEnforcing && !session.IsAway)
            {
                state = OverlayState.Drift;
                dot = true;
                border = "Drift";
                MinutesText = minutes;
                TaskText = $"{drift.Label} for {OverlayTimeText.Left(now - drift.Since, false).Replace("less than 1 min", "a moment")}";
                Line2 = "Back to " + (Checklist is { HasTasks: true } ? "the task" : ProfileName);
            }
            else if (session.IsAway)
            {
                state = OverlayState.Away;
                accent = "Muted";
                MinutesText = session.Away == AwayKind.SideTask ? "Side task" : "Paused";
                TaskText = StepAway?.AwayText ?? string.Empty;
                Line2 = "the countdown waits";
            }
            else if (session.IsPlanning)
            {
                state = OverlayState.Planning;
                accent = "Muted";
                minutesInk = "Muted";
                MinutesText = minutes;
                TaskText = "to plan";
                Line2 = "no nudges yet";
            }
            else if (!session.IsEnforcing)
            {
                state = OverlayState.Break;
                accent = "Sleep";
                MinutesText = minutes;
                TaskText = "Break";
                Line2 = Checklist is { NextTaskTitle.Length: > 0 } ? $"next: {Checklist.NextTaskTitle}" : $"{EndsText}";
            }
            else if (hasEnd && left <= WrapUpLeft && left > TimeSpan.Zero)
            {
                state = OverlayState.WrapUp;
                accent = "Ink";
                MinutesText = minutes;
                TaskText = task;
                Line2 = EndsText + progress;
            }
            else
            {
                state = OverlayState.Focus;
                MinutesText = minutes;
                TaskText = task;
                Line2 = EndsText + progress + next;
            }

            if (waiting.Length > 0 && state is OverlayState.Focus or OverlayState.WrapUp)
            {
                Line2 = waiting;
            }

            // The timeline: done, left, then a break and what is next (their lengths are indicative: the engine plans only the running phase).
            double done = hasEnd ? (end!.Value - start).TotalSeconds - Math.Max(0, left.TotalSeconds) : 0;
            double phase = hasEnd ? (end!.Value - start).TotalSeconds : 1;
            timelineLeft = hasEnd ? Math.Max(0, left.TotalSeconds) : 0;
            TimelineDone = Math.Max(0, done);
            TimelineLeft = timelineLeft;
            TimelineBreak = hasEnd && session.IsEnforcing ? phase * 0.12 : 0;
            TimelineNext = hasEnd && session.IsEnforcing ? phase * 0.18 : 0;
            RingValue = settings.FillUp ? 1 - Fraction : Fraction;
            ShowsPause = true;
            PauseIcon = session.IsAway ? MaterialIconKind.Play : MaterialIconKind.Pause;
            ShowsDone = session.IsEnforcing && !session.IsPlanning && !session.IsAway;
            ShowsStart = false;
        }
        else
        {
            Left = TimeSpan.Zero;
            Fraction = 0;
            EndsText = string.Empty;
            RingValue = 0;
            TimelineDone = TimelineLeft = TimelineBreak = TimelineNext = 0;
            ShowsPause = ShowsDone = false;
            (string Title, DateTime LocalStart)? meeting = Actions.NextMeeting();
            if (Day.ShowsSleep)
            {
                state = OverlayState.WindDown;
                border = "Sleep";
                accent = "Sleep";
                MinutesText = "Wind down";
                TaskText = string.Empty;
                Line2 = Day.SleepText;
            }
            else if (meeting is { } soon && soon.LocalStart > local && soon.LocalStart - local <= MeetingSoonWithin)
            {
                state = OverlayState.MeetingSoon;
                MinutesText = $"Meeting in {OverlayTimeText.Left(soon.LocalStart - local + TimeSpan.FromSeconds(59), false)}";
                TaskText = string.Empty;
                Line2 = soon.Title;
                accent = "Upcoming";
            }
            else if (Actions.IsWorkDone())
            {
                state = OverlayState.WorkDone;
                accent = "Met";
                RingValue = 1;
                MinutesText = "Work done";
                TaskText = string.Empty;
                Line2 = GoalText.Length > 0 ? GoalText : "Goal met";
            }
            else
            {
                state = OverlayState.Between;
                minutesInk = "Muted";
                MinutesText = DoneNotice is { Length: > 0 } notice ? notice : Timers is { HasLines: true } ? Timers.Lines[0].Name : "Nothing running";
                TaskText = string.Empty;
                Line2 = waiting.Length > 0 ? waiting : Timers is { HasLines: true } ? Timers.Lines[0].TimeText : Day.StatusText;
                ShowsStart = true;
            }
        }

        State = state;
        ShowsDriftDot = state == OverlayState.Drift;
        ShowsInlineDot = ShowsDriftDot && !ShowsRing;
        ShowsDot = dot || HasNudgeDot;
        DotBrush = OverlayBrushes.Get(dot ? "Drift" : "Waiting");
        BorderBrush = OverlayBrushes.Get(now < GlowUntil ? "Ink" : border);
        RingBrush = OverlayBrushes.Get(accent);
        MinutesBrush = OverlayBrushes.Get(minutesInk);
        OnPropertyChanged(nameof(HasBattery));
    }

    private void RefreshBatteries()
    {
        var rows = new List<OverlayBatteryRow>();
        if (_battery.Current is { } own && _visibility.IsShown("this-pc", BatterySurface.CollapsedOverlay))
        {
            rows.Add(Row("this-pc", MaterialIconKind.Laptop, own.Percent, own.IsCharging));
        }

        foreach ((string id, string _, int percent, string kind) in Actions.OtherBatteries())
        {
            if (_visibility.IsShown(id, BatterySurface.CollapsedOverlay))
            {
                rows.Add(Row(id, kind == "watch" ? MaterialIconKind.Watch : MaterialIconKind.Cellphone, percent, false));
            }
        }

        if (rows.Count == Batteries.Count && rows.Zip(Batteries).All(pair => pair.First == pair.Second))
        {
            return;
        }

        Batteries.Clear();
        foreach (OverlayBatteryRow row in rows)
        {
            Batteries.Add(row);
        }
    }

    private static OverlayBatteryRow Row(string id, MaterialIconKind icon, int percent, bool charging)
    {
        return new OverlayBatteryRow(id, charging ? MaterialIconKind.BatteryCharging : icon, $"{percent}%", OverlayBrushes.Get(percent <= 20 && !charging ? "Drift" : "Ink"));
    }

    private void RefreshSound()
    {
        TrackText = Noise?.TrackText ?? string.Empty;
        IsPlaying = Noise?.IsPlaying ?? false;
    }
}
