using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The title bar's session control (Tasks redesign `03` §A). Idle: "Start session" (Ctrl+Shift+S), which opens the app's usual start
/// controls (<see cref="QuickSessionService"/>'s popup). Running: a ring with the time left (dashed when the session counts up) and,
/// on hover or keyboard focus, Step away, the end time and the session menu. Away: "Away 6 min" with I'm back. Ended: "Ended ·
/// switches next time" until Tasks is next opened (<see cref="SessionLayoutService"/>). Every action goes through the services the
/// rest of the app uses: ending early is <see cref="SessionEndFlow"/> (wait and reason), stepping away and pausing are
/// <see cref="StepAwayService"/>; nothing here shortens a wait or unlocks anything. Times come from the session's trusted clock.
/// Refreshed on every session tick. Use from the UI thread only.
/// </summary>
public sealed partial class SessionControlViewModel : ObservableObject
{
    // What "Add 10 minutes" adds to the running focus phase (the same as Today's "+10 min").
    private static readonly TimeSpan ExtendBy = TimeSpan.FromMinutes(10);

    private readonly SessionController _sessions;
    private readonly StepAwayService _stepAway;
    private readonly SessionLayoutService _layout;
    private readonly Action _openStart;
    private readonly Func<Task> _endSession;

    /// <param name="openStart">The app's start controls (the quick-session popup: profile, length, tasks, Start).</param>
    /// <param name="endSession">The app's end flow (<see cref="SessionEndFlow"/>: a wait and a reason when the session needs them).</param>
    public SessionControlViewModel(
        SessionController sessions,
        StepAwayService stepAway,
        SessionLayoutService layout,
        Action openStart,
        Func<Task> endSession)
    {
        _sessions = sessions;
        _stepAway = stepAway;
        _layout = layout;
        _openStart = openStart;
        _endSession = endSession;

        _sessions.StateChanged += (_, _) => Refresh();
        _sessions.Ticked += (_, _) => Refresh();
        _stepAway.Changed += (_, _) => Refresh();
        _layout.Changed += (_, _) => Refresh();
        Refresh();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsEnded), nameof(IsRunningOrAway), nameof(ForcesOpen))]
    public partial SessionControlState State { get; set; }

    public bool IsIdle => State == SessionControlState.Idle;

    public bool IsEnded => State == SessionControlState.Ended;

    /// <summary>The ring pill shows (running, paused or away).</summary>
    public bool IsRunningOrAway => State is SessionControlState.Running or SessionControlState.Away;

    /// <summary>Away: I'm back stays in view without hovering.</summary>
    public bool ForcesOpen => State == SessionControlState.Away;

    /// <summary>The big part, in the number font: "32 min", "Away 6 min" or "Ended".</summary>
    [ObservableProperty]
    public partial string PrimaryText { get; set; } = string.Empty;

    /// <summary>The muted part after it: "left", "left · paused", "so far", "32 min left" (away) or "switches next time".</summary>
    [ObservableProperty]
    public partial string SecondaryText { get; set; } = string.Empty;

    /// <summary>Shown on hover: "ends 1:23 AM", "since 9:00 AM", "break until 1:23 AM" or "paused 3 min".</summary>
    [ObservableProperty]
    public partial string EndsText { get; set; } = string.Empty;

    /// <summary>
    /// The title bar's compact extras (owner 2026-11-14, smaller reserve): <see cref="EndsText"/> without AM/PM, "ends 11:20"; the
    /// full text stays in its tooltip and the Session menu's heading.
    /// </summary>
    [ObservableProperty]
    public partial string EndsShortText { get; set; } = string.Empty;

    /// <summary>The ring has an end to count down to: the accent arc shows the share of the phase left.</summary>
    [ObservableProperty]
    public partial bool HasRing { get; set; }

    /// <summary>No set end: a dashed ring (the session counts up).</summary>
    [ObservableProperty]
    public partial bool IsCountUp { get; set; }

    /// <summary>The share of this phase still to go, 1 down to 0 (frozen while the countdown is).</summary>
    [ObservableProperty]
    public partial double RingValue { get; set; }

    /// <summary>A break: the ring uses the sleep colour, as on Today.</summary>
    [ObservableProperty]
    public partial bool IsBreak { get; set; }

    /// <summary>"Pause timer" is on: the ring fades and shows a pause sign; the main button is Resume.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RingOpacity))]
    public partial bool IsPaused { get; set; }

    /// <summary>The ring fades while the timer is paused.</summary>
    public double RingOpacity => IsPaused ? 0.55 : 1;

    /// <summary>"Step away", "I'm back" or "Resume".</summary>
    [ObservableProperty]
    public partial string MainActionText { get; set; } = "Step away";

    [ObservableProperty]
    public partial MaterialIconKind MainActionIcon { get; set; } = MaterialIconKind.HumanWalk;

    /// <summary>The menu's line under Step away: whether the timer keeps running (ADR 77); empty for I'm back and Resume.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMainActionSub))]
    public partial string MainActionSubText { get; set; } = string.Empty;

    public bool HasMainActionSub => MainActionSubText.Length > 0;

    /// <summary>"Pause timer" is offered: in focus or planning, not away or paused already.</summary>
    [ObservableProperty]
    public partial bool CanPause { get; set; }

    /// <summary>"Side task…" is offered: not away.</summary>
    [ObservableProperty]
    public partial bool CanSideTask { get; set; }

    /// <summary>"Add 10 minutes" is offered: a timed manual focus phase, not away (the same rule as Today's "+10 min").</summary>
    [ObservableProperty]
    public partial bool CanExtend { get; set; }

    /// <summary>"End test" for a test session (it ends at once), else "End session…".</summary>
    [ObservableProperty]
    public partial string EndSessionText { get; set; } = "End session…";

    /// <summary>"End and mark the rest unfinished" is offered: a real session with the task list (its open tasks go back to Queued).</summary>
    [ObservableProperty]
    public partial bool CanEndToQueued { get; set; }

    /// <summary>
    /// The app's end flow asked to send the open tasks back to Queued when it ends (<see cref="SessionEndFlow"/> with that outcome: the
    /// same wait and reason). Null: the entry isn't offered.
    /// </summary>
    public Func<Task>? EndToQueued { get; set; }

    /// <summary>Whether the session's tasks can go back to Queued now (the task list is the source and a real session runs).</summary>
    public Func<bool>? OffersEndToQueued { get; set; }

    /// <summary>The ended pill's tip: "Session ended at 12:52 AM · click to switch Tasks back now".</summary>
    [ObservableProperty]
    public partial string EndedTip { get; set; } = string.Empty;

    /// <summary>"Start session" or Ctrl+Shift+S: the app's start controls, the same ones the global shortcut opens. Only with no session running.</summary>
    [RelayCommand]
    private void Start()
    {
        if (_sessions.State.IsActive)
        {
            return;
        }

        _openStart();
    }

    /// <summary>The main button: Resume while paused, I'm back while away, else Step away (a break, ADR 77's setting).</summary>
    [RelayCommand]
    private void MainAction()
    {
        SessionState state = _sessions.State;
        if (!state.IsActive)
        {
            return;
        }

        if (state.IsAway)
        {
            _stepAway.Return();
        }
        else
        {
            _stepAway.StepAway(AwayKind.Break);
        }

        Refresh();
    }

    /// <summary>"Pause timer" (ADR 76): the countdown freezes until Resume; enforcement keeps running.</summary>
    [RelayCommand]
    private void PauseTimer()
    {
        _stepAway.PauseTimer();
        Refresh();
    }

    /// <summary>"Side task…": the away card asks how long (5, 10 or 15 minutes), as on Today.</summary>
    [RelayCommand]
    private void SideTask()
    {
        _stepAway.AskSideTaskLength();
        Refresh();
    }

    /// <summary>"Add 10 minutes": the running timed focus phase ends 10 minutes later (the stricter direction).</summary>
    [RelayCommand]
    private void ExtendFocus()
    {
        _sessions.ExtendFocus(ExtendBy);
        Refresh();
    }

    /// <summary>"End session…": the app's end flow, with its wait and reason when the session needs them. Never a way around it.</summary>
    [RelayCommand]
    private Task EndSessionAsync()
    {
        return _endSession();
    }

    /// <summary>"End and mark the rest unfinished": the same end flow (wait and reason); when it ends, the open tasks go back to Queued.</summary>
    [RelayCommand]
    private Task EndToQueuedAsync()
    {
        return EndToQueued?.Invoke() ?? Task.CompletedTask;
    }

    /// <summary>A click on "Ended": Tasks switches back to planning now (as "Switch now" on its notice).</summary>
    [RelayCommand]
    private void ClearEnded()
    {
        _layout.SwitchNow();
    }

    /// <summary>Reads the session again (also every second).</summary>
    public void Refresh()
    {
        SessionState state = _sessions.State;
        DateTimeOffset now = _sessions.Now;
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        if (!state.IsActive)
        {
            RefreshIdle(now, zone);
            return;
        }

        State = state.IsAway && !state.IsPaused ? SessionControlState.Away : SessionControlState.Running;
        IsPaused = state.IsPaused;
        IsBreak = !state.IsEnforcing;
        EndSessionText = state.IsTest ? "End test" : "End session…";
        CanPause = state.IsEnforcing && !state.IsAway;
        CanSideTask = !state.IsAway;
        CanExtend = state.CanExtendFocus;
        CanEndToQueued = !state.IsTest && EndToQueued is not null && OffersEndToQueued?.Invoke() == true;

        DateTimeOffset clock = state.CountdownTime(now);
        RefreshRing(state, clock);
        RefreshTexts(state, now, clock, zone);
        RefreshMainAction(state);
    }

    private void RefreshIdle(DateTimeOffset now, TimeZoneInfo zone)
    {
        IsPaused = IsBreak = HasRing = IsCountUp = false;
        RingValue = 0;
        CanPause = CanSideTask = CanExtend = CanEndToQueued = false;
        EndsText = EndsShortText = string.Empty;
        if (_layout.EndedAt is DateTimeOffset endedAt)
        {
            State = SessionControlState.Ended;
            PrimaryText = "Ended";
            SecondaryText = "switches next time";
            EndedTip = $"Session ended at {Clock(endedAt, zone)} · click to switch Tasks back now";
            return;
        }

        State = SessionControlState.Idle;
        PrimaryText = SecondaryText = EndedTip = string.Empty;
    }

    // The ring: the share of the phase left (as on Today), or a dashed ring for a session with no set end.
    private void RefreshRing(SessionState state, DateTimeOffset clock)
    {
        if (state.PhaseEndsAt is DateTimeOffset end && state.PhaseStartedAt is DateTimeOffset start && end > start)
        {
            HasRing = true;
            IsCountUp = false;
            RingValue = Math.Clamp((end - clock) / (end - start), 0, 1);
            return;
        }

        HasRing = false;
        IsCountUp = state.PhaseEndsAt is null;
        RingValue = 0;
    }

    private void RefreshTexts(SessionState state, DateTimeOffset now, DateTimeOffset clock, TimeZoneInfo zone)
    {
        bool hasEnd = state.PhaseEndsAt is not null;
        string time = state.PhaseEndsAt is DateTimeOffset phaseEnd
            ? Minutes(phaseEnd - clock, roundUp: true)
            : Minutes(clock - (state.PhaseStartedAt ?? now), roundUp: false);
        string phase = state.IsPlanning ? " · planning" : !state.IsEnforcing ? " · break" : string.Empty;

        if (State == SessionControlState.Away)
        {
            string away = state.Away == AwayKind.SideTask ? "Side task" : "Away";
            PrimaryText = $"{away} {AwayMinutes(state, now)}";
            SecondaryText = hasEnd ? $"{time} left{phase}" : $"{time} so far{phase}";
        }
        else
        {
            PrimaryText = time;
            SecondaryText = (hasEnd ? "left" : "so far") + (state.IsPaused ? " · paused" : phase);
        }

        EndsText = EndsFor(state, now, zone, at => Clock(at, zone));
        EndsShortText = EndsFor(state, now, zone, at => ShortClock(at, zone));
    }

    private static string EndsFor(SessionState state, DateTimeOffset now, TimeZoneInfo zone, Func<DateTimeOffset, string> clock)
    {
        return state.IsPaused ? $"paused {AwayMinutes(state, now)}"
            : state.IsPlanning && state.FocusEndsAt(now) is DateTimeOffset focusEnd ? $"ends {clock(focusEnd)}"
            : state.PhaseEndsAt is DateTimeOffset end ? (state.IsEnforcing ? $"ends {clock(end)}" : $"break until {clock(end)}")
            : $"since {clock(state.PhaseStartedAt ?? now)}";
    }

    // "11:20": 12-hour, without AM/PM (only the title bar's compact extras; the tooltip has the full time).
    private static string ShortClock(DateTimeOffset at, TimeZoneInfo zone)
    {
        return TimeZoneInfo.ConvertTime(at, zone).ToString("h:mm", CultureInfo.InvariantCulture);
    }

    private void RefreshMainAction(SessionState state)
    {
        if (state.IsPaused)
        {
            MainActionText = "Resume";
            MainActionIcon = MaterialIconKind.Play;
            MainActionSubText = string.Empty;
        }
        else if (state.IsAway)
        {
            MainActionText = "I’m back";
            MainActionIcon = MaterialIconKind.AccountArrowLeftOutline;
            MainActionSubText = string.Empty;
        }
        else
        {
            MainActionText = "Step away";
            MainActionIcon = MaterialIconKind.HumanWalk;
            MainActionSubText = StepAwayTimerPolicy.KeepsTimerRunning(_stepAway.StepAwaySetting(), AwayKind.Break)
                ? "Timer keeps running · logged as away"
                : "Timer pauses · logged as away";
        }
    }

    // "6 min" away or paused, at least 1 (as the prototype counts it).
    private static string AwayMinutes(SessionState state, DateTimeOffset now)
    {
        TimeSpan away = state.AwaySince is DateTimeOffset since && now > since ? now - since : TimeSpan.Zero;
        return Minutes(away, roundUp: false, atLeastOne: true);
    }

    // "32 min", or "1 h 05 min" from an hour. A countdown rounds up (never "0 min left" while time is left); time so far rounds down.
    private static string Minutes(TimeSpan span, bool roundUp, bool atLeastOne = false)
    {
        double total = Math.Max(0, span.TotalMinutes);
        int minutes = roundUp ? (int)Math.Ceiling(total) : (int)Math.Floor(total);
        if (atLeastOne)
        {
            minutes = Math.Max(1, minutes);
        }

        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00} min";
    }

    // 12-hour with AM/PM on every screen (repo rule), whatever the system's setting.
    private static string Clock(DateTimeOffset at, TimeZoneInfo zone)
    {
        return TimeZoneInfo.ConvertTime(at, zone).ToString("h:mm tt", CultureInfo.InvariantCulture);
    }
}
