using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The run-into-lunch prompt (a fixed lunch period, schema v13): when a real session would overlap today's fixed lunch, or end
/// within a minute of its start, a front card asks Keep, Adjust or Flexible this time. It asks when the session starts (for the
/// length chosen, whichever way it was started: Quick Start, Start, a phone, the start-of-day assistant) and once more 5 minutes
/// before lunch if the session still runs into it. Keep leaves the session as it is: lunch starts by itself and holds it
/// (<see cref="LunchService.HoldsSessionAway"/>). Adjust, for a timed session only, moves its end to when lunch starts.
/// Flexible this time leaves today's lunch to the user's Start lunch (<see cref="LunchService.MakeFlexibleTodayAsync"/>). Never
/// in a test session, while away, after lunch started or once it was made flexible. Use from the UI thread only.
/// </summary>
public sealed class LunchRunIntoService
{
    // A session ending this close before lunch (or later) runs into it.
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);

    // The second ask comes this long before lunch.
    private static readonly TimeSpan Lead = TimeSpan.FromMinutes(5);

    private readonly SessionController _sessions;
    private readonly LunchService _lunch;
    private bool _sawRealSession;
    private bool _askedBeforeLunch;
    private DateTime _cardLunchStart;

    public LunchRunIntoService(SessionController sessions, LunchService lunch)
    {
        _sessions = sessions;
        _lunch = lunch;
        _sessions.StateChanged += OnStateChanged;
        _sessions.Ticked += OnTicked;
        _lunch.Changed += OnLunchChanged;
    }

    /// <summary>The card asking what to do, or null. At most one.</summary>
    public LunchRunIntoCardViewModel? Card { get; private set; }

    /// <summary>Raised when <see cref="Card"/> opens or closes (the app shows or closes the window).</summary>
    public event EventHandler? CardChanged;

    /// <summary>The card's window was closed some other way: it is asked again at the next check, if it is still due.</summary>
    public void CardWindowClosed()
    {
        Card = null;
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        if (!state.IsRealSession)
        {
            _sawRealSession = false;
            _askedBeforeLunch = false;
            Close();
            return;
        }

        if (_sawRealSession)
        {
            return;
        }

        // A session just began: ask now for the length chosen. One put back after a restart is only asked at the 5-minute mark.
        _sawRealSession = true;
        _askedBeforeLunch = false;
        if (!_sessions.IsResuming)
        {
            Ask(atStart: true);
        }
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        if (Card is not null)
        {
            // Lunch's time came with the card still up: it is answered by lunch itself (it starts, and holds the session).
            if (LocalNow() >= _cardLunchStart)
            {
                Close();
            }

            return;
        }

        if (_sawRealSession && !_askedBeforeLunch)
        {
            Ask(atStart: false);
        }
    }

    // Lunch started (here or on another device) or was made flexible: nothing left to ask.
    private void OnLunchChanged(object? sender, EventArgs e)
    {
        if (Card is not null && (_lunch.Taken is not null || _lunch.IsFlexibleToday))
        {
            Close();
        }
    }

    private void Ask(bool atStart)
    {
        SessionState state = _sessions.State;
        if (Card is not null || !state.IsRealSession || _lunch.AutoStartPending is not FixedLunch lunch)
        {
            return;
        }

        DateTime local = LocalNow();
        DateTime lunchStart = lunch.Window.Start;
        if (local >= lunchStart)
        {
            return;
        }

        bool inLastMinutes = local >= lunchStart - Lead;
        if (!atStart && (!inLastMinutes || state.IsAway))
        {
            return;
        }

        // A timed session has an end to compare; a session with none (open-ended, a Pomodoro) is still running at the 5-minute
        // mark, but at its start nothing says it will reach lunch.
        DateTimeOffset now = _sessions.Now;
        DateTimeOffset? end = state.Mode == SessionMode.Manual ? state.FocusEndsAt(now) : null;
        DateTime? endLocal = end is DateTimeOffset e ? TimeZoneInfo.ConvertTime(e, _sessions.TimeProvider.LocalTimeZone).DateTime : null;
        bool runsInto = endLocal is DateTime known ? known > lunchStart - Margin : !atStart;
        if (!runsInto)
        {
            return;
        }

        // Started within 5 minutes of lunch: this card is also the one before lunch.
        if (inLastMinutes)
        {
            _askedBeforeLunch = true;
        }

        _cardLunchStart = lunchStart;
        Card = new LunchRunIntoCardViewModel(Describe(lunchStart, endLocal), AdjustText(state, now, lunchStart - local, end), new LunchRunIntoActions(
            Keep: Close,
            Adjust: () =>
            {
                // At the click: lunch's start from now (the card may have waited a while).
                _sessions.ShortenFocusEnd(_sessions.Now + (lunchStart - LocalNow()));
                Close();
            },
            Flexible: () =>
            {
                _ = _lunch.MakeFlexibleTodayAsync();
                Close();
            }));
        CardChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Describe(DateTime lunchStart, DateTime? endLocal)
    {
        string session = endLocal is DateTime end
            ? end > lunchStart ? $"This session would end at {end:t}, after it." : $"This session would end at {end:t}, right before it."
            : "This session is still running then.";
        return $"Lunch starts by itself at {lunchStart:t}. {session} "
               + "Keep it (lunch holds it and it carries on after), shorten it, or start lunch yourself today.";
    }

    // Adjust only for a timed session that can end when lunch starts: strictly shorter, with at least a minute of focus.
    private string? AdjustText(SessionState state, DateTimeOffset now, TimeSpan untilLunch, DateTimeOffset? end)
    {
        if (state.Mode != SessionMode.Manual || end is not DateTimeOffset sessionEnd)
        {
            return null;
        }

        DateTimeOffset newEnd = now + untilLunch;
        DateTimeOffset focusStart = state.FocusStartsAt(now) ?? now;
        if (newEnd >= sessionEnd || newEnd - focusStart < TimeSpan.FromMinutes(1))
        {
            return null;
        }

        return $"Adjust: end at {TimeZoneInfo.ConvertTime(newEnd, _sessions.TimeProvider.LocalTimeZone):t}";
    }

    private void Close()
    {
        if (Card is null)
        {
            return;
        }

        Card = null;
        CardChanged?.Invoke(this, EventArgs.Empty);
    }

    private DateTime LocalNow() => TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
}
