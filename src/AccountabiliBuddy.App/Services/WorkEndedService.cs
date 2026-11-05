using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Work ended early (ADR 65): Start shutdown during a work part, after the end-early wait and reason, ends the rest of the current
/// work block (and, when ticked, the later blocks of the same plan date) from that moment. The ended windows count as not work
/// for every guard (<see cref="Windows"/>, fed to <see cref="DayWorkWindows"/>); "Back to work" (<see cref="BackToWorkAsync"/>)
/// undoes what is still ahead at once, with no wait, since that is stricter. Document "workEnded": per plan date the windows,
/// the last edit time and the reasons sealed with DPAPI, kept 90 days. Today's windows go in the sync file (times only) and the
/// newest edit for today from another PC is taken (<see cref="Receive"/>), like lunch. Use from the UI thread only.
/// </summary>
public sealed class WorkEndedService
{
    private const string DocumentKey = "workEnded";
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(90);

    // A plan date runs to 4:00 AM the next morning (the shutdown routine's rule).
    private static readonly TimeSpan NewDayAt = TimeSpan.FromHours(4);

    private readonly SessionController _sessions;
    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private WorkEndedState _state = new();
    private IReadOnlyList<LocalWindow> _windows = [];

    // Another PC's change that came during a session here: nothing changes during a session, so it waits until none runs.
    private SyncWorkEnded? _heldReceived;

    public WorkEndedService(SessionController sessions, SqliteJsonDocumentStore store, IDataProtector protector)
    {
        _sessions = sessions;
        _store = store;
        _protector = protector;
        _sessions.Ticked += (_, _) => ApplyHeldReceived();
    }

    /// <summary>
    /// The guarded work windows touching a range as of a moment, with the ended windows already cut out; what "a work part now"
    /// means here. Set by the app; null = no work, so nothing can be ended.
    /// </summary>
    public Func<LocalWindow, DateTime, IReadOnlyList<LocalWindow>>? WorkIn { get; set; }

    /// <summary>Raised when work is ended early, undone, or taken from another PC (guards, timelines and the sync file follow).</summary>
    public event EventHandler? Changed;

    /// <summary>Every ended window kept, as local times: what <see cref="DayWorkWindows"/> cuts out of the work parts.</summary>
    public IReadOnlyList<LocalWindow> Windows => _windows;

    /// <summary>"Back to work" shows: an ended window of today is still ahead or under way.</summary>
    public bool CanGoBack
    {
        get
        {
            DateTimeOffset now = _sessions.Now;
            return _state.Days?.Any(day => day.Windows.Any(window => window.End > now)) ?? false;
        }
    }

    public void LoadBlocking()
    {
        try
        {
            _state = Task.Run(() => _store.LoadAsync<WorkEndedState>(DocumentKey)).GetAwaiter().GetResult() ?? new WorkEndedState();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _state = new WorkEndedState();
        }

        _windows = ToLocal(_state);
    }

    /// <summary>What Start shutdown would end now; null outside a work part (Start shutdown then behaves as before).</summary>
    public WorkEndProposal? Propose()
    {
        if (WorkIn is null)
        {
            return null;
        }

        DateTime now = LocalNow();
        IReadOnlyList<LocalWindow> blocks = DayTimeline.Union(WorkIn(new LocalWindow(now.Date.AddDays(-1), now.Date.AddDays(2)), now));
        LocalWindow? current = blocks.Where(block => block.Contains(now)).Select(block => (LocalWindow?)block).FirstOrDefault();
        if (current is not LocalWindow block)
        {
            return null;
        }

        DateOnly planDate = PlanDateOf(now);
        DateTime planDayEnd = planDate.ToDateTime(TimeOnly.MinValue).AddDays(1) + NewDayAt;
        List<LocalWindow> later = blocks.Where(other => other.Start >= block.End && other.Start < planDayEnd).ToList();
        return new WorkEndProposal(planDate, new LocalWindow(now, block.End), later);
    }

    /// <summary>
    /// Ends work now, after the wait and the reason (the caller's dialog checked both): the rest of the block the dialog showed
    /// (<paramref name="proposal"/>) from this moment, plus its later blocks when <paramref name="alsoLater"/>. If that block is over
    /// by now (it ended during the wait), nothing is ended.
    /// </summary>
    /// <returns>False when the block the dialog showed no longer runs (nothing was ended).</returns>
    public async Task<bool> EndAsync(WorkEndProposal proposal, bool alsoLater, string reason)
    {
        DateTime localNow = LocalNow();
        if (localNow >= proposal.Current.End)
        {
            return false;
        }

        DateTimeOffset now = _sessions.Now;
        var current = new LocalWindow(localNow > proposal.Current.Start ? localNow : proposal.Current.Start, proposal.Current.End);
        IEnumerable<LocalWindow> ended = alsoLater ? [current, .. proposal.Later] : [current];
        WorkEndedDay day = DayOf(proposal.PlanDate) ?? new WorkEndedDay(proposal.PlanDate, [], now);
        var sealedReason = new WorkEndedReason(now, Convert.ToBase64String(_protector.Protect(reason.Trim())));
        day = day with
        {
            Windows = [.. day.Windows, .. ended.Select(ToOffset)],
            EditedAt = now,
            Reasons = [.. day.Reasons ?? [], sealedReason],
        };
        await SaveAsync(WithDay(day, now));
        return true;
    }

    /// <summary>
    /// "Back to work": what is still ahead of every ended window is work again at once (stricter, so no wait); the stretch already
    /// past stays ended.
    /// </summary>
    public async Task BackToWorkAsync()
    {
        DateTimeOffset now = _sessions.Now;
        List<WorkEndedDay> days = [];
        foreach (WorkEndedDay day in _state.Days ?? [])
        {
            if (!day.Windows.Any(window => window.End > now))
            {
                days.Add(day);
                continue;
            }

            List<WorkEndedWindow> kept = day.Windows
                .Where(window => window.Start < now)
                .Select(window => window.End > now ? window with { End = now } : window)
                .ToList();
            days.Add(day with { Windows = kept, EditedAt = now });
        }

        await SaveAsync(_state with { Days = days });
    }

    /// <summary>Today's plan date's ended windows for the sync file (times only); null when nothing was ended or undone today.</summary>
    public SyncWorkEnded? ForSync()
    {
        DateOnly today = PlanDateOf(LocalNow());
        return DayOf(today) is WorkEndedDay day
            ? new SyncWorkEnded(day.Date, day.Windows.Select(window => new SyncWorkEndedWindow(window.Start, window.End)).ToList(), day.EditedAt)
            : null;
    }

    /// <summary>
    /// Takes another PC's work ended early when it is for today's plan date and its edit is newer than ours. During a session here
    /// it is held (the newest one) and taken once no session runs.
    /// </summary>
    /// <returns>True when it was taken.</returns>
    public bool Receive(SyncWorkEnded received)
    {
        if (_sessions.State.IsActive)
        {
            if (_heldReceived is null || received.EditedAt > _heldReceived.EditedAt)
            {
                _heldReceived = received;
            }

            return false;
        }

        DateTimeOffset now = _sessions.Now;
        if (received.Date != PlanDateOf(LocalNow()) || received.EditedAt > now + TimeSpan.FromMinutes(2))
        {
            return false;
        }

        WorkEndedDay? ours = DayOf(received.Date);
        if (ours is not null && ours.EditedAt >= received.EditedAt)
        {
            return false;
        }

        List<WorkEndedWindow> windows = received.Windows
            .Where(window => window.End > window.Start)
            .Select(window => new WorkEndedWindow(window.Start, window.End))
            .ToList();
        var day = new WorkEndedDay(received.Date, windows, received.EditedAt, ours?.Reasons);
        _ = SaveAsync(WithDay(day, now));
        return true;
    }

    private void ApplyHeldReceived()
    {
        if (_heldReceived is not SyncWorkEnded held || _sessions.State.IsActive)
        {
            return;
        }

        _heldReceived = null;
        Receive(held);
    }

    private WorkEndedDay? DayOf(DateOnly date)
    {
        return _state.Days?.FirstOrDefault(day => day.Date == date);
    }

    // The state with [day] in place of its date's entry, dropping the days past KeepFor.
    private WorkEndedState WithDay(WorkEndedDay day, DateTimeOffset now)
    {
        DateOnly oldest = DateOnly.FromDateTime((now - KeepFor).Date);
        List<WorkEndedDay> days = (_state.Days ?? [])
            .Where(other => other.Date != day.Date && other.Date >= oldest)
            .Append(day)
            .OrderBy(other => other.Date)
            .ToList();
        return _state with { Days = days };
    }

    private async Task SaveAsync(WorkEndedState state)
    {
        _state = state;
        _windows = ToLocal(state);
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await _store.SaveAsync(DocumentKey, state);
        }
        catch (SqliteException)
        {
            // Kept in memory for this run; the next change writes again.
        }
    }

    private IReadOnlyList<LocalWindow> ToLocal(WorkEndedState state)
    {
        return (state.Days ?? [])
            .SelectMany(day => day.Windows)
            .Where(window => window.End > window.Start)
            .Select(window => new LocalWindow(ToLocalTime(window.Start), ToLocalTime(window.End)))
            .ToList();
    }

    private WorkEndedWindow ToOffset(LocalWindow window)
    {
        TimeZoneInfo zone = _sessions.TimeProvider.LocalTimeZone;
        return new WorkEndedWindow(
            new DateTimeOffset(window.Start, zone.GetUtcOffset(window.Start)),
            new DateTimeOffset(window.End, zone.GetUtcOffset(window.End)));
    }

    private DateTime ToLocalTime(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, _sessions.TimeProvider.LocalTimeZone).DateTime;

    private DateTime LocalNow() => ToLocalTime(_sessions.Now);

    private static DateOnly PlanDateOf(DateTime now) => ShutdownService.PlanDateOf(now);
}
