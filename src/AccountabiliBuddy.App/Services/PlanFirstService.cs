using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Plan first (redesign phase 2, reworked in phase 11): a session begins with a planning stage of <see cref="Length"/>
/// (<see cref="SessionPhase.Planning"/>), before the focus countdown. Planning time isn't focus time: the focus phase starts
/// fresh, for its full length, when planning ends. Unknown activity is noted, not asked about, and escalation for it doesn't
/// start (<see cref="EnforcementEngine.HoldUnknownsUntil"/>); rules still enforce. Planning ends on "I'm ready", when the
/// checklist's first task is ticked, or on <b>Start</b> on the "Ready?" card; when its time is up a chime and the overlay glow
/// call and the card asks (Start, +2, +5 or +15 minutes), and unanswered the card stays and planning keeps counting (still not
/// focus). <see cref="Ended"/> then hands over what was noted (the stacked prompt, or the review when the session is over).
/// Time stepped away isn't planning time (the engine moves the stage's end like a phase's), a resumed session carries on in
/// the stage it was in, and a test session skips planning. On a fresh install the first session learns instead
/// (<see cref="LearnModeService"/>): no planning stage, the hold lasts the whole session, "I'm ready" and ticked tasks don't
/// end it, and what it saw goes to the review card. Use from the UI thread only.
/// </summary>
public sealed class PlanFirstService : IDisposable
{
    public static readonly TimeSpan Length = TimeSpan.FromMinutes(7);

    /// <summary>The extra planning minutes the "Ready?" card offers.</summary>
    public static readonly int[] ExtraMinutes = [2, 5, 15];

    // An unanswered card comes back to the front this often, like the away card.
    private static readonly TimeSpan CardRepeat = TimeSpan.FromMinutes(2);

    // Time up chimes only when it just happened: a stage that ran out while the app was closed asks without a sound.
    private static readonly TimeSpan MaxLateness = TimeSpan.FromMinutes(1);

    private readonly SessionController _sessions;
    private readonly SessionTasksService? _tasks;
    private readonly LearnModeService? _learn;
    private readonly List<ActivitySnapshot> _held = [];
    private readonly HashSet<string> _heldSubjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTimeOffset _startedAt;
    private readonly ReadyCardActions _cardActions;
    private bool _planning;
    private bool _learning;
    private bool _wasActive;
    private bool _sawSession;
    private int _doneAtStart;

    // How many +N the stage has had, and the count the time-up chime already played for. A step away moves the stage's end but
    // is not an extension, so it never chimes twice; a +N re-arms the chime for the new end.
    private int _extensions;
    private int _chimedFor = -1;
    private DateTimeOffset _cardRaisedAt;

    // When planning ended in the session still running: unknowns' grace counts from then (null once the session ends).
    private DateTimeOffset? _endedAt;

    public PlanFirstService(SessionController sessions, SessionTasksService? tasks, LearnModeService? learn = null)
    {
        _sessions = sessions;
        _tasks = tasks;
        _learn = learn;
        _startedAt = sessions.Now;
        _cardActions = new ReadyCardActions(Start: Ready, Extend: Extend);
        _sessions.PlanningLength = () => _learn?.IsPending == true ? null : Length;
        _sessions.StateChanged += OnStateChanged;
        _sessions.Ticked += OnTicked;
        if (_tasks is not null)
        {
            _tasks.Changed += OnTasksChanged;
        }
    }

    /// <summary>
    /// True while unknowns are held: a session's planning stage, or a fresh install's first session (which learns). Planning
    /// has no end of its own, so it lasts past its time until the user answers.
    /// </summary>
    public bool IsPlanning => _planning || _learning;

    /// <summary>True during the planning stage (the phase before the focus countdown), not while a first session learns.</summary>
    public bool InStage => _planning;

    /// <summary>True while a fresh install's first session learns (a hold that lasts until the session ends).</summary>
    public bool IsLearning => _learning;

    /// <summary>When the planning stage ends (moves later after a step away or +N); null when not in the stage.</summary>
    public DateTimeOffset? EndsAt => _planning && _sessions.State.IsPlanning ? _sessions.State.PhaseEndsAt : null;

    /// <summary>When the planning stage began; null when not in the stage.</summary>
    public DateTimeOffset? StartedAt => _planning && _sessions.State.IsPlanning ? _sessions.State.PhaseStartedAt : null;

    /// <summary>Planning's time is up and the user hasn't answered "Ready?": its time keeps counting, and it isn't focus.</summary>
    public bool IsTimeUp => EndsAt is DateTimeOffset end && _sessions.State.CountdownTime(_sessions.Now) >= end;

    /// <summary>
    /// What the engine holds unknowns until: a distant time while planning or learning, then the moment planning ended (so an
    /// unknown seen while planning gets its full grace from then), null once the session is over.
    /// </summary>
    public DateTimeOffset? HoldUntil => IsPlanning ? DateTimeOffset.MaxValue : _endedAt;

    /// <summary>E.g. "4:12"; "0:00" once time is up; empty when not in the stage.</summary>
    public string LeftText
    {
        get
        {
            if (EndsAt is not DateTimeOffset end)
            {
                return string.Empty;
            }

            TimeSpan left = end - _sessions.State.CountdownTime(_sessions.Now);
            if (left < TimeSpan.Zero)
            {
                left = TimeSpan.Zero;
            }

            return $"{(int)left.TotalMinutes}:{left.Seconds:00}";
        }
    }

    /// <summary>Unknown sites and apps seen while planning, one per subject, oldest first. Cleared when a session starts.</summary>
    public IReadOnlyList<ActivitySnapshot> HeldUnknowns => _held;

    /// <summary>The "Ready?" card asking whether to start the focus, or null. At most one, never outside a planning stage.</summary>
    public ReadyCardViewModel? Card { get; private set; }

    /// <summary>Raised when <see cref="Card"/> opens or closes, and when an unanswered one is due again (the app shows or raises the window).</summary>
    public event EventHandler? CardChanged;

    /// <summary>Raised when <see cref="HeldUnknowns"/> gains an item or is emptied (a session beginning), for Today's "Seen while planning" card.</summary>
    public event EventHandler? HeldUnknownsChanged;

    /// <summary>Raised once when planning ends (the session may have ended too).</summary>
    public event EventHandler? Ended;

    /// <summary>Raised every second while planning and when it starts or ends (for "Planning · 4:12 left").</summary>
    public event EventHandler? Changed;

    /// <summary>Raised once per planning end when its time is up and it's fresh (the app plays the focus-end chime and glows the overlay).</summary>
    public event EventHandler? TimeUp;

    /// <summary>The name of the task in front, for the card's line; empty without one. Set by the app.</summary>
    public Func<string> CurrentTaskName { get; set; } = () => string.Empty;

    /// <summary>Notes an unknown site or app seen while planning.</summary>
    public void NoteUnknown(ActivitySnapshot snapshot)
    {
        if (IsPlanning && _heldSubjects.Add(EnforcementEngine.ClassificationSubject(snapshot)))
        {
            _held.Add(snapshot);
            HeldUnknownsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>"I'm ready" or Start on the card: planning ends now and the focus countdown starts fresh.</summary>
    public void Ready()
    {
        if (_planning && !_learning)
        {
            _sessions.EndPlanning();
        }
    }

    /// <summary>+2, +5 or +15 on the card: planning goes on for that long more (still not focus).</summary>
    public void Extend(int minutes)
    {
        if (_planning && ExtraMinutes.Contains(minutes))
        {
            if (_sessions.ExtendPlanning(TimeSpan.FromMinutes(minutes)))
            {
                _extensions++;
            }
        }
    }

    /// <summary>Runs every second: the time-up chime and card, and the first task ticked while away on return.</summary>
    public void CheckNow()
    {
        SessionState state = _sessions.State;
        if (!_planning || !state.IsPlanning || state.IsAway)
        {
            return;
        }

        // The first task ticked: the plan is made (also one ticked during a step away, once back).
        if (_tasks is not null && _tasks.Current.DoneCount > _doneAtStart)
        {
            Ready();
            return;
        }

        if (state.PhaseEndsAt is not DateTimeOffset end || state.CountdownTime(_sessions.Now) < end)
        {
            return;
        }

        DateTimeOffset now = _sessions.Now;
        if (_chimedFor != _extensions)
        {
            _chimedFor = _extensions;
            if (end > _startedAt && now - end <= MaxLateness)
            {
                TimeUp?.Invoke(this, EventArgs.Empty);
            }
        }

        if (Card is null)
        {
            OpenCard();
        }
        else if (now - _cardRaisedAt >= CardRepeat)
        {
            _cardRaisedAt = now;
            CardChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _sessions.StateChanged -= OnStateChanged;
        _sessions.Ticked -= OnTicked;
        if (_tasks is not null)
        {
            _tasks.Changed -= OnTasksChanged;
        }
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        // A test session has no planning stage (it would cover a short test) and doesn't use up the first session's learning.
        if (state.IsTest)
        {
            return;
        }

        if (!state.IsActive)
        {
            _wasActive = false;
            End(state);
            _endedAt = null;
            if (_sawSession)
            {
                _ = _learn?.FinishAsync(); // Also a session resumed after a crash: learning happens once.
            }

            return;
        }

        _sawSession = true;
        bool justStarted = !_wasActive;
        _wasActive = true;

        if (state.IsPlanning)
        {
            if (!_planning)
            {
                Begin(learning: false);
            }

            // A step away closes the card (the away card asks instead; it comes back on the first tick after Back on track), and
            // so does +2 / +5 / +15, which makes the time up no more.
            if (state.IsAway || !IsTimeUp)
            {
                CloseCard();
            }

            return;
        }

        if (_planning)
        {
            End(state); // The stage ended: the focus countdown began.
            return;
        }

        if (justStarted && !_sessions.IsResuming && !_learning && state.Phase == SessionPhase.Focus && state.CompletedFocusCycles == 0
            && _learn?.IsPending == true)
        {
            // A fresh install's first session: no planning stage, the hold lasts the whole session.
            Begin(learning: true);
        }
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        CheckNow();
        if (IsPlanning)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTasksChanged(object? sender, EventArgs e)
    {
        CheckNow();
    }

    private void Begin(bool learning)
    {
        bool hadHeld = _held.Count > 0;
        _held.Clear();
        _heldSubjects.Clear();
        if (hadHeld)
        {
            HeldUnknownsChanged?.Invoke(this, EventArgs.Empty);
        }

        _endedAt = null;
        _extensions = 0;
        _chimedFor = -1;
        _doneAtStart = _tasks?.Current.DoneCount ?? 0;
        _learning = learning;
        _planning = !learning;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void End(SessionState state)
    {
        if (!IsPlanning)
        {
            return;
        }

        _endedAt = state.IsActive && state.PhaseStartedAt is DateTimeOffset focusStart ? focusStart : _sessions.Now;
        _planning = false;
        bool wasLearning = _learning;
        _learning = false;
        CloseCard();
        if (wasLearning)
        {
            _ = _learn?.FinishAsync();
        }

        Ended?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OpenCard()
    {
        Card = new ReadyCardViewModel(CurrentTaskName().Trim(), _cardActions);
        _cardRaisedAt = _sessions.Now;
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
}
