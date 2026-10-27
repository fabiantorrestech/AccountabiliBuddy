using System.Net;
using System.Text.Json;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>
/// The user's Beeminder goals, read only (ADR 43). With the personal auth token pasted in Integrations → Beeminder
/// (kept DPAPI-encrypted in the document "beeminder" with the last answer), the goals are read through
/// <see cref="CalendarNetwork"/> about every 30 minutes and when Today opens. Nothing is ever written to Beeminder.
/// Removing the token stops the calls and forgets the goals, so they leave this PC's sync file too. The token is never
/// logged, shown or sent anywhere else. Use from the UI thread only.
/// </summary>
public sealed class BeeminderService : IDisposable
{
    private const string DocumentKey = "beeminder";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);
    // Opening Today asks again only when the last attempt (answered or not) is older than this, so tab switching
    // doesn't hammer Beeminder, also while it fails.
    private static readonly TimeSpan TodayOpenMinimumAge = TimeSpan.FromMinutes(5);
    // After Beeminder says "too many requests", wait this long before asking again.
    private static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(30);

    private readonly CalendarNetwork _network;
    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private Stored _state = Stored.Empty;
    private DispatcherTimer? _timer;
    private bool _busy;
    private DateTimeOffset _notBefore = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastAttemptAt;
    // Beeminder refused the token: no more automatic reads until the user presses Test or saves a token.
    private bool _tokenRejected;
    // Bumped when the token changes, so an answer to an older token's request is dropped.
    private int _generation;

    public BeeminderService(CalendarNetwork network, SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _network = network;
        _store = store;
        _protector = protector;
        _time = time;
    }

    public bool HasToken => _state.ProtectedToken is not null;

    /// <summary>The goals from the last answer, soonest due first; empty without a token.</summary>
    public IReadOnlyList<BeeminderGoal> Goals => _state.Goals ?? [];

    /// <summary>When Beeminder last answered; null before the first answer.</summary>
    public DateTimeOffset? FetchedAt => _state.FetchedAt;

    /// <summary>Why the last read failed ("Check your token", no network, rate limited); null when it worked.</summary>
    public string? Problem { get; private set; }

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            _state = Task.Run(() => _store.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult() ?? Stored.Empty;
        }
        catch (Exception ex) when (ex is SqliteException or JsonException)
        {
            _state = Stored.Empty;
        }
    }

    /// <summary>Starts the 30-minute refresh and reads the goals once now (only with a token).</summary>
    public void Start()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = RefreshInterval };
            _timer.Tick += (_, _) => _ = RefreshAsync(userAsked: false);
            _timer.Start();
        }

        _ = RefreshAsync(userAsked: false);
    }

    /// <summary>Today opened: reads again when the last attempt (answered or not) is a few minutes old.</summary>
    public void RefreshIfStale()
    {
        if (HasToken && (_lastAttemptAt is not DateTimeOffset attempted || _time.GetUtcNow() - attempted >= TodayOpenMinimumAge))
        {
            _ = RefreshAsync(userAsked: false);
        }
    }

    /// <summary>
    /// Tries a pasted token and keeps it only when Beeminder accepts it.
    /// </summary>
    /// <returns>Null when saved; otherwise what to tell the user.</returns>
    public async Task<string?> SaveTokenAsync(string token)
    {
        string trimmed = token.Trim();
        if (trimmed.Length == 0)
        {
            return "Paste your token first.";
        }

        if (_busy)
        {
            return "Still asking Beeminder; try again in a moment.";
        }

        _busy = true;
        try
        {
            int generation = ++_generation;
            (IReadOnlyList<BeeminderGoal>? goals, string? problem, _) = await ReadAsync(trimmed);
            if (goals is null || generation != _generation)
            {
                return problem ?? "Beeminder didn't answer; try again.";
            }

            Problem = null;
            _tokenRejected = false;
            await SaveAsync(new Stored(Convert.ToBase64String(_protector.Protect(trimmed)), goals.ToList(), _time.GetUtcNow()));
            return null;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Reads the goals now with the saved token. Nothing is asked without a token, while Beeminder asked to wait, or,
    /// unless the user asked (Test), after Beeminder refused the token.
    /// </summary>
    public async Task<BeeminderRefresh> RefreshAsync(bool userAsked = false)
    {
        if (!HasToken)
        {
            return BeeminderRefresh.NoToken;
        }

        if (_busy)
        {
            return BeeminderRefresh.Busy;
        }

        if (_time.GetUtcNow() < _notBefore || (_tokenRejected && !userAsked))
        {
            return BeeminderRefresh.Waiting;
        }

        if (Token() is not string token)
        {
            // DPAPI can't open it (e.g. the data folder came from another user or PC): nothing to ask with.
            Problem = "Paste your token again: this PC can't read the saved one.";
            Changed?.Invoke(this, EventArgs.Empty);
            return BeeminderRefresh.Failed;
        }

        _busy = true;
        try
        {
            int generation = _generation;
            (IReadOnlyList<BeeminderGoal>? goals, string? problem, bool rejected) = await ReadAsync(token);
            if (generation != _generation || !HasToken)
            {
                return BeeminderRefresh.Failed;
            }

            Problem = problem;
            _tokenRejected = rejected;
            if (goals is null)
            {
                // The last answer stays; the card says when it's from.
                Changed?.Invoke(this, EventArgs.Empty);
                return BeeminderRefresh.Failed;
            }

            await SaveAsync(_state with { Goals = goals.ToList(), FetchedAt = _time.GetUtcNow() });
            return BeeminderRefresh.Read;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Forgets the token and the goals: no more calls, and the summary leaves the sync file.</summary>
    public Task RemoveAsync()
    {
        _generation++;
        Problem = null;
        _notBefore = DateTimeOffset.MinValue;
        _lastAttemptAt = null;
        _tokenRejected = false;
        return SaveAsync(Stored.Empty);
    }

    /// <summary>The derived summary for this PC's sync file (ADR 43); null without a token or goals.</summary>
    public IReadOnlyList<SyncBeeminderGoal>? SyncSummary() =>
        HasToken && Goals.Count > 0
            ? Goals.Select(goal => new SyncBeeminderGoal(goal.Title, goal.Needed, goal.SafeDays, goal.DueAt)).ToList()
            : null;

    public void Dispose()
    {
        _timer?.Stop();
    }

    // Rejected = Beeminder refused this token (401/403).
    private async Task<(IReadOnlyList<BeeminderGoal>? Goals, string? Problem, bool Rejected)> ReadAsync(string token)
    {
        _lastAttemptAt = _time.GetUtcNow();
        try
        {
            using JsonDocument answer = await _network.GetBeeminderGoalsAsync(token, CancellationToken.None);
            return (BeeminderGoalsParser.Parse(answer.RootElement, _time.GetUtcNow()), null, false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return (null, "Check your token: Beeminder didn't accept it.", true);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _notBefore = _time.GetUtcNow() + RateLimitPause;
            return (null, "Beeminder asked to wait; trying again later.", false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode status && (int)status >= 500)
        {
            return (null, "Beeminder had a problem; trying again later.", false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (null, "Couldn't reach Beeminder; trying again later.", false);
        }
        catch (JsonException)
        {
            return (null, "Beeminder's answer couldn't be read; trying again later.", false);
        }
    }

    private string? Token() =>
        _state.ProtectedToken is string sealedToken ? Unprotect(sealedToken) : null;

    private string? Unprotect(string sealedToken)
    {
        try
        {
            return _protector.Unprotect(Convert.FromBase64String(sealedToken));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private Task SaveAsync(Stored state)
    {
        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
        return _store.SaveAsync(DocumentKey, state);
    }

    private sealed record Stored(string? ProtectedToken, List<BeeminderGoal>? Goals, DateTimeOffset? FetchedAt)
    {
        public static Stored Empty { get; } = new(null, null, null);
    }
}
