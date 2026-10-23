using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Plan first (redesign phase 2): a session's first <see cref="Length"/> are for planning. Unknown activity is noted, not
/// asked about, and escalation for it doesn't start (<see cref="EnforcementEngine.HoldUnknownsUntil"/>); rules still
/// enforce. It ends after <see cref="Length"/> of the first phase (time stepped away doesn't count), on "I'm ready", when
/// the checklist's first task is ticked, or when the session ends. <see cref="Ended"/> then hands over what was noted (the
/// stacked prompt, or the review when the session is over). A session resumed after a restart doesn't plan again. On a
/// fresh install the first session learns instead (<see cref="LearnModeService"/>): the hold lasts the whole session, "I'm
/// ready" and ticked tasks don't end it, and what it saw goes to the review card. Use from the UI thread only.
/// </summary>
public sealed class PlanFirstService : IDisposable
{
    public static readonly TimeSpan Length = TimeSpan.FromMinutes(7);

    private readonly SessionController _sessions;
    private readonly SessionTasksService? _tasks;
    private readonly LearnModeService? _learn;
    private readonly List<ActivitySnapshot> _held = [];
    private readonly HashSet<string> _heldSubjects = new(StringComparer.OrdinalIgnoreCase);
    private bool _planning;
    private bool _learning;
    private bool _sawSession;
    private int _doneAtStart;

    // When planning ended in the session still running: unknowns' grace counts from then (null once the session ends).
    private DateTimeOffset? _endedAt;

    public PlanFirstService(SessionController sessions, SessionTasksService? tasks, LearnModeService? learn = null)
    {
        _sessions = sessions;
        _tasks = tasks;
        _learn = learn;
        _sessions.StateChanged += OnStateChanged;
        _sessions.Ticked += OnTicked;
        if (_tasks is not null)
        {
            _tasks.Changed += OnTasksChanged;
        }
    }

    /// <summary>True during a session's planning minutes.</summary>
    public bool IsPlanning => _planning;

    /// <summary>True while a fresh install's first session learns (planning that lasts until the session ends).</summary>
    public bool IsLearning => _planning && _learning;

    /// <summary>When planning ends (moves later after a step away); null when not planning, or learning (no set end).</summary>
    public DateTimeOffset? EndsAt => _planning && !_learning && _sessions.State.PhaseStartedAt is DateTimeOffset start ? start + Length : null;

    /// <summary>
    /// What the engine holds unknowns until: the planned end while planning, then the moment planning ended (so an unknown
    /// seen while planning gets its full grace from then), null once the session is over.
    /// </summary>
    public DateTimeOffset? HoldUntil => _planning ? (_learning ? DateTimeOffset.MaxValue : EndsAt) : _endedAt;

    /// <summary>E.g. "4:12"; empty when not planning.</summary>
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

    /// <summary>Raised once when planning ends (the session may have ended too).</summary>
    public event EventHandler? Ended;

    /// <summary>Raised every second while planning (for "Planning · 4:12 left") and when it starts or ends.</summary>
    public event EventHandler? Changed;

    /// <summary>Notes an unknown site or app seen while planning.</summary>
    public void NoteUnknown(ActivitySnapshot snapshot)
    {
        if (_planning && _heldSubjects.Add(EnforcementEngine.ClassificationSubject(snapshot)))
        {
            _held.Add(snapshot);
        }
    }

    /// <summary>"I'm ready": planning ends now.</summary>
    public void Ready()
    {
        if (!_learning)
        {
            End();
        }
    }

    /// <summary>Ends planning once its time is up. Runs every second.</summary>
    public void CheckNow()
    {
        if (!_planning || _learning)
        {
            return; // Learning ends only with the session.
        }

        SessionState state = _sessions.State;
        bool stillFirstFocus = state.Phase == SessionPhase.Focus && state.CompletedFocusCycles == 0;
        if (!stillFirstFocus || (EndsAt is DateTimeOffset end && state.CountdownTime(_sessions.Now) >= end))
        {
            End();
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
        if (!state.IsActive)
        {
            End();
            _endedAt = null;
            if (_sawSession)
            {
                _ = _learn?.FinishAsync(); // Also a session resumed after a crash: learning happens once.
            }

            return;
        }

        _sawSession = true;

        if (!_planning && state.Phase == SessionPhase.Focus && state.CompletedFocusCycles == 0 && !_sessions.IsResuming
            && state.PhaseStartedAt is DateTimeOffset start && _sessions.Now - start < TimeSpan.FromSeconds(5))
        {
            // A session just started (not resumed, not a step away coming back): plan first.
            _held.Clear();
            _heldSubjects.Clear();
            _endedAt = null;
            _doneAtStart = _tasks?.Current.DoneCount ?? 0;
            _learning = _learn?.IsPending == true;
            _planning = true;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        CheckNow();
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        CheckNow();
        if (_planning)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTasksChanged(object? sender, EventArgs e)
    {
        // The first task ticked: the plan is made.
        if (_planning && !_learning && _tasks is not null && _tasks.Current.DoneCount > _doneAtStart)
        {
            End();
        }
    }

    private void End()
    {
        if (!_planning)
        {
            return;
        }

        DateTimeOffset now = _sessions.State.CountdownTime(_sessions.Now);
        _endedAt = EndsAt is DateTimeOffset end && end < now ? end : now;
        _planning = false;
        if (_learning)
        {
            _learning = false;
            _ = _learn?.FinishAsync();
        }

        Ended?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
