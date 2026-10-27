using System.Security.Cryptography;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Pre-Work parts (ADR 31). The first time the user is active in one with no session running, "Is today a work day?"
/// opens. On a work day, until the part ends or a session starts, the work-hours apps and the default profile's
/// distracting apps and sites are minimized. A day off takes a ten-minute wait spent planning the day, then frees this
/// computer for the day. An emergency unlock (a long reason and a retyped code, two a week) lifts Pre-Work until the
/// part ends. Use from the UI thread only.
/// </summary>
public sealed class PreWorkService
{
    public static readonly TimeSpan DayOffWait = TimeSpan.FromMinutes(10);
    public const int UnlocksPerWeek = 2;
    public const int MinReasonLength = 40;

    private const string DocumentKey = "prework";
    private static readonly TimeSpan ActiveWithin = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan NoticeGap = TimeSpan.FromMinutes(1);

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly IUserIdleDetector _idle;
    private readonly IEnforcer _enforcer;
    private readonly RuleEvaluator _rules;
    private readonly Func<IReadOnlyList<ActivitySnapshot>> _current;
    private readonly Func<FocusProfile?> _defaultProfile;
    private readonly Func<IReadOnlyCollection<string>> _workApps;
    private readonly Action<string, string> _notify;
    private PreWorkState _state = PreWorkState.Empty;
    private DateTimeOffset _lastNotice = DateTimeOffset.MinValue;
    private bool _wasGuarding;

    public PreWorkService(
        SessionController sessions, ConfigurationService configuration, SqliteJsonDocumentStore store, IDataProtector protector,
        IUserIdleDetector idle, IEnforcer enforcer, Func<IReadOnlyList<ActivitySnapshot>> current, Func<FocusProfile?> defaultProfile,
        Func<IReadOnlyCollection<string>> workApps, Action<string, string> notify)
    {
        _sessions = sessions;
        _configuration = configuration;
        _store = store;
        _protector = protector;
        _idle = idle;
        _enforcer = enforcer;
        _rules = new RuleEvaluator(new RuleMatcher());
        _current = current;
        _defaultProfile = defaultProfile;
        _workApps = workApps;
        _notify = notify;
        _sessions.Ticked += (_, _) => CheckNow();
    }

    /// <summary>The question or the day-off wait, while it shows; null otherwise.</summary>
    public PreWorkPromptViewModel? Prompt { get; private set; }

    /// <summary>Raised when <see cref="Prompt"/> opens or closes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when today became a day off here (the work-hours guard and the start nudge stand down).</summary>
    public event EventHandler? DayOffTaken;

    /// <summary>Today is a day off on this computer.</summary>
    public bool IsDayOff => _state.DayOffOn == Today();

    /// <summary>True while work tools and distractions are held back.</summary>
    public bool IsGuarding => CurrentPart() is not null && _state.WorkDayOn == Today() && Holds();

    /// <summary>Emergency unlocks in the last 7 days.</summary>
    public int UnlocksThisWeek => (_state.Unlocks ?? []).Count(unlock => unlock.At > _sessions.Now - TimeSpan.FromDays(7));

    public void LoadBlocking()
    {
        try
        {
            _state = Task.Run(() => _store.LoadAsync<PreWorkState>(DocumentKey)).GetAwaiter().GetResult() ?? PreWorkState.Empty;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _state = PreWorkState.Empty;
        }
    }

    /// <summary>Opens or closes the question, ends Pre-Work when a session starts, sweeps when guarding begins. Runs every second.</summary>
    public void CheckNow()
    {
        DateOnly today = Today();
        LocalWindow? part = CurrentPart();
        // A test session never ends Pre-Work.
        if (part is not null && _sessions.State.IsRealSession && _state.EndedOn != today)
        {
            _ = SaveAsync(_state with { EndedOn = today });
        }

        bool asking = part is not null && !_sessions.State.IsActive && _state.WorkDayOn != today && !IsDayOff && Holds()
            && (Prompt is not null || _idle.GetIdleTime() < ActiveWithin);
        if (asking && Prompt is null)
        {
            Prompt = new PreWorkPromptViewModel(part!.Value.End, _state.DayOffAskedAt is DateTimeOffset asked && DateOnly.FromDateTime(Local(asked)) == today ? asked : null, this);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else if (!asking && Prompt is not null)
        {
            Close();
        }

        Prompt?.Refresh(_sessions.Now, UnlocksThisWeek);
        bool guarding = IsGuarding || Prompt is not null;
        if (guarding && !_wasGuarding)
        {
            // Guarding just began: what's already on screen waits too.
            foreach (ActivitySnapshot shown in _current())
            {
                OnActivity(shown);
            }
        }

        _wasGuarding = guarding;
    }

    /// <summary>A window came to the front: work tools and distractions wait while guarding (and while the question shows).</summary>
    public void OnActivity(ActivitySnapshot snapshot)
    {
        if (!(IsGuarding || Prompt is not null) || snapshot.IsEmptyDesktop || IsOwnApp(snapshot))
        {
            return;
        }

        bool workTool = _workApps().Contains(snapshot.ProcessName.ToLowerInvariant());
        bool distracting = _defaultProfile() is FocusProfile profile
            && _rules.Evaluate(profile, snapshot).Classification == Classification.Distracting;
        if (!workTool && !distracting)
        {
            return;
        }

        _ = _enforcer.MinimizeWindowAsync(snapshot.WindowId);
        DateTimeOffset now = _sessions.Now;
        if (now - _lastNotice >= NoticeGap && CurrentPart() is LocalWindow part)
        {
            _lastNotice = now;
            _notify($"Pre-work until {part.End:t}", workTool
                ? $"{snapshot.ProcessName} waits until work starts. Ease in: plan, read, get ready."
                : "That's a distraction. Ease in: plan, read, get ready.");
        }
    }

    /// <summary>"Yes, a work day": Pre-Work holds until the part ends or a session starts.</summary>
    public async Task AnswerWorkDayAsync()
    {
        await SaveAsync(_state with { WorkDayOn = Today() });
        Close();
    }

    /// <summary>"No, a day off": the wait starts now.</summary>
    public async Task AskDayOffAsync()
    {
        DateTimeOffset now = _sessions.Now;
        await SaveAsync(_state with { DayOffAskedAt = now });
        Prompt?.StartDayOffWait(now);
    }

    /// <summary>The wait is over: today is a day off here; the planned lines become come-backs.</summary>
    public async Task TakeDayOffAsync(IEnumerable<string> plans, Func<string, Task> addComeback)
    {
        if (_state.DayOffAskedAt is not DateTimeOffset asked || _sessions.Now - asked < DayOffWait)
        {
            return;
        }

        foreach (string line in plans.Select(line => line.Trim()).Where(line => line.Length > 0))
        {
            await addComeback(line);
        }

        await SaveAsync(_state with { DayOffOn = Today() });
        Close();
        DayOffTaken?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// "No, a day off" undone (tapped by mistake), during the wait or after: back to a normal day, asked again at the next
    /// activity in a Pre-Work part. Being stricter needs no wait.
    /// </summary>
    public async Task CancelDayOffAsync()
    {
        await SaveAsync(_state with { DayOffAskedAt = null, DayOffOn = null });
    }

    /// <summary>A new random code to retype for an emergency unlock (12 letters and digits, no look-alikes).</summary>
    public static string NewUnlockCode()
    {
        const string letters = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 12).Select(_ => letters[RandomNumberGenerator.GetInt32(letters.Length)]).ToArray());
    }

    /// <summary>An emergency unlock: refused without a long enough reason, the right code, or when the week's two are used.</summary>
    /// <returns>Null when unlocked; otherwise why not.</returns>
    public async Task<string?> EmergencyUnlockAsync(string reason, string typedCode, string shownCode)
    {
        if (UnlocksThisWeek >= UnlocksPerWeek)
        {
            return $"Both emergency unlocks of the last 7 days are used.";
        }

        if (reason.Trim().Length < MinReasonLength)
        {
            return $"The reason needs at least {MinReasonLength} characters.";
        }

        if (!string.Equals(typedCode.Trim(), shownCode, StringComparison.Ordinal))
        {
            return "The code doesn't match. Type it exactly as shown.";
        }

        if (CurrentPart() is not LocalWindow part)
        {
            return null;
        }

        var unlock = new EmergencyUnlock(_sessions.Now, Convert.ToBase64String(_protector.Protect(reason.Trim())));
        DateTimeOffset until = new DateTimeOffset(part.End, TimeZoneInfo.Local.GetUtcOffset(part.End));
        await SaveAsync(_state with { UnlockedUntil = until, Unlocks = [.. _state.Unlocks ?? [], unlock] });
        Close();
        _notify("Unlocked until the part ends",
            "If you need this computer or your phone unguarded at certain times, schedule an off day instead: Calendar tab → one-off days.");
        return null;
    }

    private void Close()
    {
        if (Prompt is null)
        {
            return;
        }

        Prompt = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Not ended by a session, not a day off, not unlocked.
    private bool Holds()
    {
        DateOnly today = Today();
        return _state.EndedOn != today && _state.DayOffOn != today
            && !(_state.UnlockedUntil is DateTimeOffset until && _sessions.Now < until);
    }

    private LocalWindow? CurrentPart()
    {
        DateTime now = Local(_sessions.Now);
        DayPlan plan = _configuration.Current.DayPlan;
        return plan.PartsIn(new LocalWindow(now.Date.AddDays(-1), now.Date.AddDays(2)))
            .Where(part => part.Category.Behavior == CategoryBehavior.PreWork && part.Window.Start <= now && now < part.Window.End)
            .Select(part => (LocalWindow?)part.Window)
            .FirstOrDefault();
    }

    private static bool IsOwnApp(ActivitySnapshot snapshot) =>
        snapshot.ProcessName.StartsWith("AccountabiliBuddy", StringComparison.OrdinalIgnoreCase);

    private DateOnly Today() => DateOnly.FromDateTime(Local(_sessions.Now));

    private DateTime Local(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, _sessions.TimeProvider.LocalTimeZone).DateTime;

    private async Task SaveAsync(PreWorkState state)
    {
        _state = state;
        await _store.SaveAsync(DocumentKey, state);
    }
}
