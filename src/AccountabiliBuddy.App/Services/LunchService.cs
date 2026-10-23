using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The flexible lunch (schema v8, design part B): when today's plan has a flexible break, "Start lunch" (on any of the
/// user's devices) starts it for its length, 1 hour by default. A running session steps away as a Break for it.
/// Not started by its hint time: one hint; never started by the end of its window: skipped (the window stays work).
/// When it's over: one chime and "Lunch over!". A lunch started on another device arrives in its sync file
/// (<see cref="SyncFile.Lunch"/>) and is taken here too. Document "lunch". Use from the UI thread only.
/// </summary>
public sealed class LunchService
{
    private const string DocumentKey = "lunch";

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly SqliteJsonDocumentStore _store;
    private readonly Action<string, string> _remind;
    private readonly Action _chime;
    private LunchState _state = new();

    public LunchService(SessionController sessions, ConfigurationService configuration, SqliteJsonDocumentStore store, Action<string, string> remind, Action chime)
    {
        _sessions = sessions;
        _configuration = configuration;
        _store = store;
        _remind = remind;
        _chime = chime;
        _sessions.Ticked += (_, _) => CheckNow();
    }

    /// <summary>Raised when lunch starts or ends (the Today card, the day bar and the sync file follow).</summary>
    public event EventHandler? Changed;

    /// <summary>Today's flexible break from the plan; null on a day without one.</summary>
    public FlexibleBreak? Today => _configuration.Current.DayPlan.FlexibleBreakOn(DateOnly.FromDateTime(LocalNow()));

    /// <summary>Today's lunch once started (it may be over); null before it starts.</summary>
    public SyncLunch? Taken => _state.Start is DateTimeOffset start && _state.End is DateTimeOffset end && IsToday(start) ? new SyncLunch(start, end) : null;

    /// <summary>True while lunch runs.</summary>
    public bool IsOnLunch => Taken is SyncLunch lunch && _sessions.Now < lunch.End;

    /// <summary>"Start lunch" shows: today has a flexible break and no lunch was taken yet.</summary>
    public bool CanStart => Today is not null && Taken is null;

    public void LoadBlocking()
    {
        try
        {
            _state = Task.Run(() => _store.LoadAsync<LunchState>(DocumentKey)).GetAwaiter().GetResult() ?? new LunchState();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _state = new LunchState();
        }
    }

    /// <summary>Starts lunch now for today's length (1 hour without a flexible break); a running session steps away.</summary>
    public async Task StartAsync()
    {
        DateTimeOffset now = _sessions.Now;
        TimeSpan length = Today?.Length ?? TimeSpan.FromHours(1);
        await SaveAsync(_state with { Start = now, End = now + length, OverTold = false });
        if (_sessions.State.IsActive && !_sessions.State.IsAway)
        {
            _sessions.StepAway(AwayKind.Break);
        }
    }

    /// <summary>Back early: lunch ends now.</summary>
    public async Task EndNowAsync()
    {
        if (IsOnLunch)
        {
            await SaveAsync(_state with { End = _sessions.Now, OverTold = true });
        }
    }

    /// <summary>A lunch another device started (its sync file): taken here when it's today's and newer than ours.</summary>
    public void Receive(SyncLunch lunch)
    {
        if (!IsToday(lunch.Start) || (_state.Start is DateTimeOffset ours && IsToday(ours) && ours >= lunch.Start && _state.End == lunch.End))
        {
            return;
        }

        if (_state.Start is DateTimeOffset mine && IsToday(mine) && mine > lunch.Start)
        {
            return;
        }

        _ = SaveAsync(_state with { Start = lunch.Start, End = lunch.End, OverTold = _sessions.Now >= lunch.End });
    }

    /// <summary>The hint, and "Lunch over!" once. Runs every second.</summary>
    public void CheckNow()
    {
        DateTimeOffset now = _sessions.Now;
        if (Taken is SyncLunch lunch && now >= lunch.End && !_state.OverTold)
        {
            _ = SaveAsync(_state with { OverTold = true });
            _chime();
            _remind("Lunch over!", "Welcome back. Plan your next task and start a session.");
            return;
        }

        DateTime local = LocalNow();
        DateOnly today = DateOnly.FromDateTime(local);
        if (Today is FlexibleBreak flex && Taken is null && flex.HintAt is DateTime hint && local >= hint && local < flex.Window.End
            && _state.HintedOn != today)
        {
            _ = SaveAsync(_state with { HintedOn = today });
            _remind("Time for lunch?", $"Lunch isn't started yet. Take it before {flex.Window.End:t}: Start lunch on Today or your phone.");
        }
    }

    private bool IsToday(DateTimeOffset time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, _sessions.TimeProvider.LocalTimeZone).DateTime) == DateOnly.FromDateTime(LocalNow());

    private DateTime LocalNow() => TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;

    private async Task SaveAsync(LunchState state)
    {
        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
        await _store.SaveAsync(DocumentKey, state);
    }
}

/// <summary>Today's lunch on this computer (document "lunch"): when it started and ends, whether "Lunch over!" came, and the last hint's day.</summary>
public sealed record LunchState(DateTimeOffset? Start = null, DateTimeOffset? End = null, bool OverTold = false, DateOnly? HintedOn = null);
