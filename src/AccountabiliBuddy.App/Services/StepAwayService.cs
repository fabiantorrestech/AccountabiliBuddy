using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Step away during a session: the countdown pauses (<see cref="SessionController.StepAway"/>) until the user says they are
/// back on track: with the shortcut again, "I'm back", or on the "Back to the task?" card that the first input after
/// stepping away brings up (a key press, click or scroll, or the mouse moved a real distance: a bumped desk or a twitch
/// doesn't count). Input alone never ends it. "Still away" brings the card back 2 minutes later; unanswered it is raised
/// again every 2 minutes. A side task is the same pause for 5, 10 or 15 minutes with enforcement still running; when its
/// time is up a chime and a card ask "Back to the task?", and the side time keeps growing until the user answers. None of
/// this shortens a wait or ends the session. Enforcement holds as it was only until the first input after a break
/// (<see cref="SessionState.AwayAtDesk"/>); from then on, and through a side task, it runs as in focus. Optionally steps away by itself after a few idle minutes (off by default).
/// Use from the UI thread only (the shortcut is marshalled here).
/// </summary>
public sealed class StepAwayService : IDisposable
{
    private const string DocumentKey = "stepAway";

    // The click or key press that stepped away lands just after it; input in this window isn't "back".
    private static readonly TimeSpan PressGrace = TimeSpan.FromSeconds(2);

    // How far (in screen pixels, straight line) the pointer must move from where it was when stepping away.
    private const int MouseBackPixels = 300;

    // "Still away", and a card nobody answered, come back after this.
    private static readonly TimeSpan CardRepeat = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan SideTaskExtra = TimeSpan.FromMinutes(5);
    private static readonly int[] SideTaskMinutes = [5, 10, 15];

    // The first +5 min needs a reason; each one after it also waits (BypassPolicy).
    private static readonly BypassSettings FirstExtensionRules = new(TimeSpan.Zero, 10);
    private static readonly BypassSettings LaterExtensionRules = new(TimeSpan.FromSeconds(30), 10);

    private readonly SessionController _sessions;
    private readonly IUserIdleDetector _idle;
    private readonly ICursorPosition _cursor;
    private readonly IGlobalHotkey _hotkey;
    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;

    // Input from before the app started (a session resumed while away) isn't "back".
    private readonly DateTimeOffset _startedAt;

    // While away: where the pointer was at the start, where it was on the last check, and the input seen then.
    private (int X, int Y)? _awayAnchor;
    private (int X, int Y)? _lastCursor;
    private DateTimeOffset _lastSeenInput;

    // The last check while HoldsAway held the away (null when it didn't); input before it isn't "back".
    private DateTimeOffset? _heldAt;

    private readonly AwayCardActions _cardActions;

    // "Still away" was answered: the card shows again then (null when it wasn't).
    private DateTimeOffset? _stillAwayUntil;
    private DateTimeOffset _cardRaisedAt;

    // The side task end already chimed for (a +5 min makes a new end). The +5 min given so far is kept in the session state.
    private DateTimeOffset? _chimedFor;

    public StepAwayService(
        SessionController sessions, IUserIdleDetector idle, ICursorPosition cursor, IGlobalHotkey hotkey, SqliteJsonDocumentStore store, TimeProvider time)
    {
        _sessions = sessions;
        _idle = idle;
        _cursor = cursor;
        _hotkey = hotkey;
        _store = store;
        _time = time;
        _startedAt = time.GetUtcNow();
        _cardActions = new AwayCardActions(
            StartSideTask: minutes => StartSideTask(minutes),
            BackOnTrack: Return,
            StillAway: StillAway,
            AddFive: AddFiveMinutes,
            AddFiveRules: () => _sessions.State.SideTaskExtensions == 0 ? FirstExtensionRules : LaterExtensionRules,
            EndSession: () => _ = EndSessionFromCardAsync(),
            Close: CloseCard);

        _hotkey.Pressed += (_, _) => Dispatcher.UIThread.Post(Toggle);
        _sessions.Ticked += OnTicked;
        _sessions.StateChanged += OnStateChanged;
    }

    public StepAwaySettings Settings { get; private set; } = StepAwaySettings.Default;

    /// <summary>Why the shortcut isn't active, or null when it is (or none is set).</summary>
    public string? HotkeyError { get; private set; }

    public bool IsAway => _sessions.State.IsAway;

    /// <summary>
    /// True while input mustn't end the away (lunch: browsing at lunch isn't being back in the session); then only the
    /// shortcut, "I'm back" or the hold lifting (lunch over or ended) brings the user back, the next input after it.
    /// It only keeps the countdown paused: it never shortens a wait or unlocks anything. Set by the app.
    /// </summary>
    public Func<bool> HoldsAway { get; set; } = () => false;

    /// <summary>True during a side task (the screens show its time in amber).</summary>
    public bool IsSideTask => _sessions.State.Away == AwayKind.SideTask;

    /// <summary>"Away · 4:12" while away, "Side task · 6:12" during a side task, else empty.</summary>
    public string AwayText => _sessions.State.AwaySince is DateTimeOffset since
        ? $"{(IsSideTask ? "Side task" : "Away")} · {Clock(_time.GetUtcNow() - since)}"
        : string.Empty;

    /// <summary>Raised when the user steps away or is back, and every second while away (for <see cref="AwayText"/>).</summary>
    public event EventHandler? Changed;

    /// <summary>The name of the task in front, for "Back to &lt;task&gt;?"; empty without one. Set by the app.</summary>
    public Func<string> CurrentTaskName { get; set; } = () => string.Empty;

    /// <summary>The normal end-early flow, for the card's "End session"; true when the session ended. Set by the app.</summary>
    public Func<Task<bool>> EndSessionAsync { get; set; } = () => Task.FromResult(false);

    /// <summary>The session's focus-end chime, played when a side task's time is up. Set by the app.</summary>
    public Action PlaySideTaskChime { get; set; } = () => { };

    /// <summary>The away card showing (a side task's length, or "Back to the task?"), or null.</summary>
    public AwayCardViewModel? Card { get; private set; }

    /// <summary>True while the end-early dialog opened from the card is up: the card's window steps aside meanwhile.</summary>
    public bool IsEndDialogOpen { get; private set; }

    /// <summary>Raised when <see cref="Card"/> opens or closes, and when an unanswered one is due again (the app shows or raises the window).</summary>
    public event EventHandler? CardChanged;

    public void LoadBlocking()
    {
        try
        {
            StepAwaySettings? loaded = Task.Run(() => _store.LoadAsync<StepAwaySettings>(DocumentKey)).GetAwaiter().GetResult();
            Settings = loaded is null ? StepAwaySettings.Default : Clamped(loaded);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Settings = StepAwaySettings.Default;
        }

        RegisterHotkey(Settings.Hotkey);
    }

    /// <returns>False with no session running or when already away.</returns>
    public bool StepAway(AwayKind kind)
    {
        return _sessions.StepAway(kind);
    }

    public void Return()
    {
        _sessions.Return();
    }

    /// <summary>The shortcut: steps away during a session, or is "I'm back" while away.</summary>
    public void Toggle()
    {
        if (IsAway)
        {
            Return();
        }
        else
        {
            StepAway(AwayKind.Break);
        }
    }

    /// <summary>
    /// "Side task" at the desk: asks how long (5, 10 or 15 minutes, 5 by default) on the away card. Nothing happens with no
    /// session running or while already away.
    /// </summary>
    public void AskSideTaskLength()
    {
        SessionState state = _sessions.State;
        if (!state.IsActive || state.IsAway)
        {
            return;
        }

        if (Card is { Kind: AwayCardKind.SideTaskLength })
        {
            RaiseCard(_time.GetUtcNow());
            return;
        }

        OpenCard(AwayCardKind.SideTaskLength, "How long? The focus clock pauses; blocking and nudges keep running.");
    }

    /// <summary>Starts a side task of 5, 10 or 15 minutes.</summary>
    /// <returns>False for any other length, with no session running or when already away.</returns>
    public bool StartSideTask(int minutes)
    {
        if (!SideTaskMinutes.Contains(minutes))
        {
            return false;
        }

        CloseCard();
        _chimedFor = null;
        return _sessions.StartSideTask(TimeSpan.FromMinutes(minutes));
    }

    /// <summary>
    /// Brings up the "Back to the task?" card on the first input after stepping away, or the side task's card when its time
    /// is up; steps away after the idle minutes when that's on. Runs every second.
    /// </summary>
    public void CheckNow()
    {
        SessionState state = _sessions.State;
        if (!state.IsActive)
        {
            return;
        }

        DateTimeOffset now = _time.GetUtcNow();
        TimeSpan idleFor = _idle.GetIdleTime();
        DateTimeOffset lastInput = now - idleFor;

        if (state.AwaySince is DateTimeOffset since)
        {
            if (state.Away == AwayKind.SideTask)
            {
                CheckSideTask(state, since, now);
                return;
            }

            if (HoldsAway())
            {
                // Input while held isn't "back"; only input after the hold lifts is, from where the pointer is then.
                _awayAnchor = null;
                _heldAt = now;
                if (Card is { Kind: AwayCardKind.BackOnTrack })
                {
                    CloseCard();
                }

                // Lunch holds the away again: enforcement holds as it did until the next input after the hold lifts.
                _sessions.SetAwayAtDesk(false);
                return;
            }

            if (Card is not null)
            {
                // Unanswered: it stays up and is raised again every 2 minutes.
                if (now - _cardRaisedAt >= CardRepeat)
                {
                    RaiseCard(now);
                }

                return;
            }

            if (_stillAwayUntil is DateTimeOffset until)
            {
                if (now >= until)
                {
                    _stillAwayUntil = null;
                    OpenCard(AwayCardKind.BackOnTrack, AwayText);
                }

                return;
            }

            if (state.AwayAtDesk)
            {
                // Input was already seen (before a restart, say) and the card was never answered: it asks again.
                OpenCard(AwayCardKind.BackOnTrack, AwayText);
                return;
            }

            DateTimeOffset ignoreUntil = (since > _startedAt ? since : _startedAt) + PressGrace;
            if (_heldAt is DateTimeOffset heldAt && heldAt > ignoreUntil)
            {
                ignoreUntil = heldAt;
            }

            if (HasNewInput(lastInput, ignoreUntil))
            {
                // Input never ends the away: it only asks. The user is at the desk, so enforcement runs as in focus from now
                // (the countdown stays paused until Back on track).
                _awayAnchor = null;
                _sessions.SetAwayAtDesk(true);
                OpenCard(AwayCardKind.BackOnTrack, AwayText);
            }

            return;
        }

        _awayAnchor = null;
        _heldAt = null;

        if (Settings.AutoAfterIdle && idleFor >= TimeSpan.FromMinutes(Settings.IdleMinutes))
        {
            // From now, not from the last input: the minutes before count as focus.
            StepAway(AwayKind.Break);
        }
    }

    // A side task's time is up: one chime per end, and the card stays on top (raised every 2 minutes) while the side time grows.
    private void CheckSideTask(SessionState state, DateTimeOffset since, DateTimeOffset now)
    {
        DateTimeOffset end = state.AwayEndsAt ?? since + TimeSpan.FromMinutes(SideTaskMinutes[0]);
        if (now < end)
        {
            return;
        }

        if (_chimedFor != end)
        {
            _chimedFor = end;

            // An end that passed before this launch (a restart in the middle) chimed already: only the card comes back.
            if (end > _startedAt)
            {
                PlaySideTaskChime();
            }
        }

        if (Card is null)
        {
            OpenCard(AwayCardKind.SideTaskOver, AwayText);
        }
        else if (now - _cardRaisedAt >= CardRepeat)
        {
            RaiseCard(now);
        }
    }

    private void OpenCard(AwayCardKind kind, string detail)
    {
        Card = new AwayCardViewModel(kind, CurrentTaskName().Trim(), detail, _cardActions, _time);
        _cardRaisedAt = _time.GetUtcNow();
        CardChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseCard(DateTimeOffset now)
    {
        _cardRaisedAt = now;
        CardChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseCard()
    {
        if (Card is null)
        {
            return;
        }

        Card = null;
        CardChanged?.Invoke(this, EventArgs.Empty);
    }

    // "Still away": the card goes and comes back in 2 minutes.
    private void StillAway()
    {
        _stillAwayUntil = _time.GetUtcNow() + CardRepeat;
        _awayAnchor = null;
        CloseCard();
    }

    // +5 min, after the card met its friction (a reason, and from the second one a wait).
    private void AddFiveMinutes(string reason)
    {
        if (_sessions.ExtendSideTask(SideTaskExtra, reason))
        {
            CloseCard();
        }
    }

    // End session: the normal end-early flow with its wait and reason; if it is cancelled the card stays.
    private async Task EndSessionFromCardAsync()
    {
        if (IsEndDialogOpen)
        {
            return;
        }

        // The card steps aside (and isn't raised) while the dialog is open, so the dialog is never under it.
        IsEndDialogOpen = true;
        CardChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await EndSessionAsync();
        }
        finally
        {
            IsEndDialogOpen = false;
            CardChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // New input with the pointer where it was = a key, click or scroll. New input with the pointer moved = mouse
    // movement: it counts only once it's MouseBackPixels from where it was when stepping away.
    private bool HasNewInput(DateTimeOffset lastInput, DateTimeOffset ignoreUntil)
    {
        (int X, int Y)? cursor = _cursor.Get();
        if (_awayAnchor is null)
        {
            _awayAnchor = cursor;
            _lastCursor = cursor;
            _lastSeenInput = ignoreUntil;
        }

        if (lastInput <= ignoreUntil || lastInput <= _lastSeenInput)
        {
            _lastCursor = cursor;
            return false;
        }

        bool moved = cursor != _lastCursor;
        _lastSeenInput = lastInput;
        _lastCursor = cursor;
        if (!moved || cursor is not (int x, int y) || _awayAnchor is not (int anchorX, int anchorY))
        {
            return true;
        }

        long dx = x - anchorX, dy = y - anchorY;
        return dx * dx + dy * dy >= (long)MouseBackPixels * MouseBackPixels;
    }

    /// <returns>Null on success, otherwise why the shortcut could not be used (the previous one stays active).</returns>
    public async Task<string?> ChangeHotkeyAsync(string text)
    {
        if (text.Trim().Length == 0)
        {
            _hotkey.Unregister();
            HotkeyError = null;
            await SaveAsync(Settings with { Hotkey = string.Empty });
            return null;
        }

        if (!HotkeyChord.TryParse(text, out HotkeyChord? chord, out string? parseError))
        {
            return parseError;
        }

        if (!_hotkey.TryRegister(chord, out string? registerError))
        {
            RegisterHotkey(Settings.Hotkey); // Put the old one back.
            return registerError;
        }

        HotkeyError = null;
        await SaveAsync(Settings with { Hotkey = chord.ToString() });
        return null;
    }

    public async Task SaveAsync(StepAwaySettings settings)
    {
        Settings = Clamped(settings);
        await _store.SaveAsync(DocumentKey, Settings);
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
        _sessions.StateChanged -= OnStateChanged;
        _hotkey.Unregister();
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        CheckNow();
        Card?.Refresh(_time.GetUtcNow(), AwayText);
        if (IsAway)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        // A card only lives as long as the question it asks: the session ended, or the away it was about is over.
        bool keepsCard = state.IsActive && (Card?.Kind switch
        {
            AwayCardKind.SideTaskLength => !state.IsAway,
            AwayCardKind.BackOnTrack => state.IsAway && state.Away != AwayKind.SideTask,
            AwayCardKind.SideTaskOver => state.Away == AwayKind.SideTask,
            _ => false,
        });
        if (!keepsCard)
        {
            CloseCard();
        }

        if (!state.IsAway)
        {
            _stillAwayUntil = null;
            _chimedFor = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RegisterHotkey(string text)
    {
        if (text.Trim().Length == 0)
        {
            HotkeyError = null;
            return;
        }

        if (!HotkeyChord.TryParse(text, out HotkeyChord? chord, out string? parseError))
        {
            HotkeyError = parseError;
            return;
        }

        HotkeyError = _hotkey.TryRegister(chord, out string? error) ? null : error;
    }

    private static StepAwaySettings Clamped(StepAwaySettings settings)
    {
        return settings with
        {
            Hotkey = settings.Hotkey ?? StepAwaySettings.Default.Hotkey,
            IdleMinutes = Math.Clamp(settings.IdleMinutes, 1, 60),
        };
    }

    private static string Clock(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";
    }
}
