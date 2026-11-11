using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// Which layout Tasks shows around a session (Tasks redesign §2, ADR 76): the session layout while a real session runs, and, once it
/// has ended, until Tasks is next opened or "Switch now" ("◌ Ended · switches next time"). Also the "Session ended at 12:52 AM" notice
/// (shown while Tasks is on screen, with a 15-second countdown ×). Data only: the title bar's session control and the strip read it
/// (M5). Nothing here touches the session itself. Times come from the trusted clock. Use from the UI thread only.
/// </summary>
public sealed class SessionLayoutService
{
    /// <summary>How long the "Session ended" notice counts down before it closes by itself.</summary>
    public static readonly TimeSpan NoticeCountdown = TimeSpan.FromSeconds(15);

    private readonly SessionController _sessions;
    private readonly TimeProvider _time;
    private bool _wasRunning;

    public SessionLayoutService(SessionController sessions, TimeProvider time)
    {
        _sessions = sessions;
        _time = time;
        _wasRunning = sessions.State.IsRealSession;
        _sessions.StateChanged += OnStateChanged;
        _sessions.SessionEnded += OnSessionEnded;
    }

    /// <summary>When the last real session ended while its layout is still showing; null once Tasks switched back (or none ended).</summary>
    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>"Ended · switches next time": a session ended and Tasks still shows its layout.</summary>
    public bool SwitchesNextTime => EndedAt is not null;

    /// <summary>Tasks shows the session layout (Compact, Shelf hidden, faded Queued): a real session runs, or one ended and hasn't switched.</summary>
    public bool InSessionLayout => _sessions.State.IsRealSession || SwitchesNextTime;

    /// <summary>The "Session ended" notice is up (until dismissed, switched, or <see cref="NoticeCountdown"/> after it was first shown).</summary>
    public bool IsNoticeShown => SwitchesNextTime && !_noticeDismissed && (_noticeShownAt is not DateTimeOffset shown || _time.GetUtcNow() - shown < NoticeCountdown);

    private DateTimeOffset? _noticeShownAt;
    private bool _noticeDismissed;

    public event EventHandler? Changed;

    /// <summary>Tasks came on screen: the notice starts its countdown the first time it is seen.</summary>
    public void NoticeSeen()
    {
        if (SwitchesNextTime && _noticeShownAt is null)
        {
            _noticeShownAt = _time.GetUtcNow();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Tasks was opened again (a new visit, not the one the session ended in): the layout goes back to planning.</summary>
    public void TasksOpened()
    {
        SwitchNow();
    }

    /// <summary>"Switch now" on the notice: the layout goes back to planning at once.</summary>
    public void SwitchNow()
    {
        if (EndedAt is null)
        {
            return;
        }

        EndedAt = null;
        _noticeShownAt = null;
        _noticeDismissed = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>× on the notice: it goes, the layout stays until the next visit.</summary>
    public void DismissNotice()
    {
        if (SwitchesNextTime && !_noticeDismissed)
        {
            _noticeDismissed = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // Ended by hand (End session, an armed end when its wait is over): back to planning at once, as the prototype's End does ("Session
    // ended · Tasks is back to planning"). A session closed for the time the PC was off (At set) or one that ran out keeps "switches next time".
    private void OnSessionEnded(object? sender, SessionEnd end)
    {
        if (end.At is null && end.Ended.IsRealSession)
        {
            SwitchNow();
        }
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        bool running = state.IsRealSession;
        if (running == _wasRunning)
        {
            return;
        }

        _wasRunning = running;
        if (running)
        {
            // A new session starts: its own layout, no notice of the last one.
            EndedAt = null;
        }
        else
        {
            EndedAt = _time.GetUtcNow();
        }

        _noticeShownAt = null;
        _noticeDismissed = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
