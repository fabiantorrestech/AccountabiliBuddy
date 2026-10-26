using System.Security.Cryptography;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Import;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Holds Cold Turkey's keys during the app's windows (ADR 39) and re-arms blocks by the day plan's rules (ADR 37):
/// every minute, and when a session, work hours or Pre-Work changes, it works out what is going on, asks
/// <see cref="ColdTurkeyHold"/> what to run and runs it through Cold Turkey's command line. The password is the app's
/// (DPAPI-encrypted) and is used only once the user has written down and retyped the spare key; a spare key used
/// (in Cold Turkey or in Emergency release) releases blocks for today and makes a new key. A new key can also be asked
/// for without using the old one (ADR 41): a reason, a retyped code and a 10-minute wait, never during a focus session,
/// at most twice in 7 days; it re-locks what the old key held and releases nothing. Document "coldturkey.keys". Use
/// from the UI thread only.
/// </summary>
public sealed class ColdTurkeyKeysService : IDisposable
{
    public const string DocumentKey = "coldturkey.keys";
    private const string KeyAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int LogMax = 30;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>ADR 41: how long a request for a new spare key waits.</summary>
    public static readonly TimeSpan NewKeyWait = TimeSpan.FromMinutes(10);

    /// <summary>ADR 41: new spare keys made in the last 7 days before more are refused.</summary>
    public const int NewKeysPerWeek = 2;

    private static readonly TimeSpan NewKeyRequestsKept = TimeSpan.FromDays(90);

    private readonly ColdTurkeyService _coldTurkey;
    private readonly IColdTurkeyControl _control;
    private readonly SessionController _sessions;
    private readonly Func<bool> _preWork;
    private readonly Func<bool> _workHours;
    private readonly Func<DayPlan> _plan;
    private readonly Func<Guid?> _deviceId;
    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _timer;
    private KeysState _state = KeysState.Empty;
    private bool _checking;

    public ColdTurkeyKeysService(
        ColdTurkeyService coldTurkey,
        IColdTurkeyControl control,
        SessionController sessions,
        Func<bool> preWorkGuarding,
        Func<bool> workHoursGuarding,
        Func<DayPlan> plan,
        Func<Guid?> deviceId,
        SqliteJsonDocumentStore store,
        IDataProtector protector,
        TimeProvider time)
    {
        _coldTurkey = coldTurkey;
        _control = control;
        _sessions = sessions;
        _preWork = preWorkGuarding;
        _workHours = workHoursGuarding;
        _plan = plan;
        _deviceId = deviceId;
        _store = store;
        _protector = protector;
        _time = time;
        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += (_, _) => CheckSoon();
        _timer.Tick += (_, _) => _ = FinishNewKeyIfDueAsync();
        _sessions.StateChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = FinishNewKeyIfDueAsync());
        _sessions.StateChanged += (_, _) => CheckSoon();
    }

    /// <summary>The blocks the user assigned, and where.</summary>
    public IReadOnlyList<HeldBlock> Held => _state.Blocks;

    /// <summary>True when the user has written down and retyped the current spare key: the password lock is in use.</summary>
    public bool HasSpareKey => _state.KeyConfirmedAt is not null && _state.ProtectedPassword is not null;

    /// <summary>A new spare key waiting to be written down and retyped (null when none).</summary>
    public string? PendingSpareKey => Unprotect(_state.ProtectedPending);

    /// <summary>"Turn blocks back on" (ADR 37): blocks turned off come back on at the day's re-enable time. On by default.</summary>
    public bool RearmOn => !_state.RearmOff;

    public async Task SetRearmAsync(bool on)
    {
        _state = _state with { RearmOff = !on };
        await SaveAsync();
    }

    /// <summary>What the app did lately, newest first ("10:02 AM · Locked Social (focus session)").</summary>
    public IReadOnlyList<string> Log => _state.Log;

    /// <summary>A notice for the user (title, text).</summary>
    public event EventHandler<(string Title, string Text)>? Notice;

    /// <summary>A new spare key needs writing down (after a spare key was used).</summary>
    public event EventHandler? SpareKeyNeeded;

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            _state = Task.Run(() => _store.LoadAsync<KeysState>(DocumentKey)).GetAwaiter().GetResult() ?? KeysState.Empty;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _state = KeysState.Empty;
        }

        // ADR 41: requests for a new key are kept 90 days (saved with the next change).
        _state = _state with { NewKeyRequests = RecentRequests().ToList() };
    }

    public void Start()
    {
        _timer.Start();
        CheckSoon();
        // A wait that ended while the app was closed finishes now.
        Dispatcher.UIThread.Post(() => _ = FinishNewKeyIfDueAsync());
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    /// <summary>Saves which blocks are held where.</summary>
    public async Task SaveHeldAsync(IReadOnlyList<HeldBlock> held)
    {
        _state = _state with { Blocks = held.ToList() };
        await SaveAsync();
        CheckSoon();
    }

    /// <summary>Makes a new spare key (not used until it's retyped) and returns it to show once.</summary>
    public async Task<string> NewSpareKeyAsync()
    {
        string key = string.Join("-", Enumerable.Range(0, 4).Select(_ =>
            new string(Enumerable.Range(0, 5).Select(_ => KeyAlphabet[RandomNumberGenerator.GetInt32(KeyAlphabet.Length)]).ToArray())));
        _state = _state with { ProtectedPending = Protect(key) };
        await SaveAsync();
        return key;
    }

    /// <summary>
    /// The user retyped the shown key: it becomes the password. Blocks still locked with an old key (a new key asked
    /// for, ADR 41) are locked again with the new one at once, so the old key stops working and nothing is released.
    /// False when it doesn't match, or when the key changed while it was being confirmed (a notice says so).
    /// </summary>
    public async Task<bool> ConfirmSpareKeyAsync(string typed)
    {
        // Never alongside a check: both run Cold Turkey commands with the password.
        while (_checking)
        {
            await Task.Delay(100);
        }

        string? confirmedProtected = _state.ProtectedPending;
        string? pending = Unprotect(confirmedProtected);
        if (pending is null || !Same(typed, pending))
        {
            return false;
        }

        _checking = true;
        try
        {
            string? old = Unprotect(_state.ProtectedPassword);
            List<string> relocked = new();
            if (old is not null)
            {
                foreach (string name in _state.PasswordHeld.ToList())
                {
                    await _control.StopAsync(name, old);
                    if (await _control.StartAsync(name, password: pending))
                    {
                        relocked.Add(name);
                    }
                    else
                    {
                        // Left on; the next check holds it again (with a timed lock or the new key).
                        _state = _state with { PasswordHeld = _state.PasswordHeld.Where(other => other != name).ToList() };
                        await _control.StartAsync(name);
                        AddLog($"Couldn't lock {name} with the new key; it's held again at the next check");
                    }
                }

                _coldTurkey.RefreshNow();
                DropUnconfirmedLocks(relocked);
            }

            // The blocks are locked with the confirmed key now, so it is the password whatever happened meanwhile.
            bool changed = _state.ProtectedPending != confirmedProtected;
            _state = _state with
            {
                ProtectedPassword = confirmedProtected,
                ProtectedPending = changed ? _state.ProtectedPending : null,
                KeyConfirmedAt = _time.GetUtcNow(),
            };
            AddLog("New spare key written down");
            await SaveAsync();
            if (changed)
            {
                Notice?.Invoke(this, ("Spare key", "Another new key was made while this one was confirmed. Write down the key shown now."));
                return false;
            }

            return true;
        }
        finally
        {
            _checking = false;
            CheckSoon();
        }
    }

    /// <summary>
    /// After re-locking with the new key: Cold Turkey's command line succeeds even when it refuses, so a block its
    /// settings don't show password-locked isn't counted as held (the next check holds it again).
    /// </summary>
    private void DropUnconfirmedLocks(IReadOnlyList<string> relocked)
    {
        if (_coldTurkey.Blocks is not ColdTurkeyBlocks blocks)
        {
            // Cold Turkey's settings can't be read: the next check sees what holds.
            foreach (string name in relocked)
            {
                AddLog($"Locked {name} with the new key");
            }

            return;
        }

        foreach (string name in relocked)
        {
            if (blocks.Blocks.FirstOrDefault(block => block.Name == name) is { PasswordLocked: true })
            {
                AddLog($"Locked {name} with the new key");
                continue;
            }

            _state = _state with { PasswordHeld = _state.PasswordHeld.Where(other => other != name).ToList() };
            AddLog($"{name} isn't locked with the new key; it's held again at the next check");
        }
    }

    /// <summary>
    /// Emergency release with the spare key: every held block is released for today (password locks stopped with the
    /// key, timed locks no longer renewed, so they end within 10 minutes) and a new key is made. False when the key
    /// doesn't match.
    /// </summary>
    public async Task<bool> EmergencyReleaseAsync(string typed)
    {
        string? password = Unprotect(_state.ProtectedPassword);
        if (password is null || !Same(typed, password))
        {
            return false;
        }

        var released = new HashSet<string>(_state.Released, StringComparer.Ordinal);
        released.UnionWith(_state.Blocks.Select(block => block.Name));
        released.UnionWith(_state.PasswordHeld);
        if (_sessions.State.ProfileId is Guid profile)
        {
            released.UnionWith(_coldTurkey.ExpectedFor(profile));
        }

        _state = _state with { Released = released.ToList(), ReleasedOn = Today() };
        AddLog("Emergency release: blocks released for today");
        await SaveAsync();
        await CheckAsync();
        await RotateAsync(keepHeld: false);
        return true;
    }

    /// <summary>ADR 41: when the asked-for new key arrives (null when none is asked for).</summary>
    public DateTimeOffset? NewKeyReadyAt => _state.PendingNewKeyAt + NewKeyWait;

    /// <summary>ADR 41: how long until the asked-for new key arrives, by the trusted clock (null when none is asked for).</summary>
    public TimeSpan? NewKeyTimeLeft => NewKeyReadyAt is DateTimeOffset ready
        ? TimeSpan.FromTicks(Math.Max(0, (ready - _time.GetUtcNow()).Ticks))
        : null;

    /// <summary>ADR 41: new spare keys made in the last 7 days.</summary>
    public int NewKeysThisWeek
    {
        get
        {
            DateTimeOffset weekAgo = _time.GetUtcNow() - TimeSpan.FromDays(7);
            return Requests.Count(request => request.MadeAt is DateTimeOffset made && made > weekAgo);
        }
    }

    /// <summary>ADR 41: why a new spare key can't be asked for now (null when it can).</summary>
    public string? NewKeyRefusal
    {
        get
        {
            if (!HasSpareKey)
            {
                return "There's no spare key in use yet, so there's nothing to replace.";
            }

            if (_state.ProtectedPending is not null)
            {
                return "A new spare key is already waiting to be written down.";
            }

            if (InFocusSession)
            {
                return "Not during a focus session, breaks included. End the session first (with its own wait and reason).";
            }

            if (NewKeysThisWeek >= NewKeysPerWeek)
            {
                return $"You've had {NewKeysPerWeek} new spare keys in the last 7 days. Try again later.";
            }

            return null;
        }
    }

    /// <summary>
    /// ADR 41: asks for a new spare key. Refused without a long enough reason, the right code, during a focus session,
    /// or when the week's two are used; otherwise the 10-minute wait starts (it survives a restart).
    /// </summary>
    /// <returns>Null when the wait started; otherwise why not.</returns>
    public async Task<string?> RequestNewKeyAsync(string reason, string typedCode, string shownCode)
    {
        if (_state.PendingNewKeyAt is not null)
        {
            return "A new spare key is already on its way.";
        }

        if (NewKeyRefusal is string refusal)
        {
            return refusal;
        }

        if (reason.Trim().Length < PreWorkService.MinReasonLength)
        {
            return $"The reason needs at least {PreWorkService.MinReasonLength} characters.";
        }

        if (!string.Equals(typedCode.Trim(), shownCode, StringComparison.Ordinal))
        {
            return "The code doesn't match. Type it exactly as shown.";
        }

        DateTimeOffset now = _time.GetUtcNow();
        List<NewKeyRequest> requests = RecentRequests().ToList();
        requests.Add(new NewKeyRequest(now, Protect(reason.Trim()), null));
        _state = _state with { NewKeyRequests = requests, PendingNewKeyAt = now };
        AddLog("New spare key requested");
        await SaveAsync();
        return null;
    }

    /// <summary>ADR 41: stops the wait for a new spare key; the current key stays.</summary>
    public async Task CancelNewKeyAsync()
    {
        if (_state.PendingNewKeyAt is null)
        {
            return;
        }

        _state = _state with { PendingNewKeyAt = null };
        AddLog("New spare key request cancelled");
        await SaveAsync();
    }

    /// <summary>
    /// ADR 41: once the wait is over and no focus session runs (one started meanwhile holds the key until it ends),
    /// makes the new key to write down and retype. The current key keeps working until the new one is retyped.
    /// </summary>
    /// <returns>True when a new key was made.</returns>
    public async Task<bool> FinishNewKeyIfDueAsync()
    {
        DateTimeOffset now = _time.GetUtcNow();
        // A focus session (breaks included) holds the key until it ends; a confirm in progress is left alone.
        if (NewKeyReadyAt is not DateTimeOffset ready || now < ready || InFocusSession || _checking)
        {
            return false;
        }

        DateTimeOffset? requestedAt = _state.PendingNewKeyAt;
        List<NewKeyRequest> requests = Requests
            .Select(request => request.At == requestedAt ? request with { MadeAt = now } : request)
            .ToList();
        _state = _state with { NewKeyRequests = requests, PendingNewKeyAt = null };
        AddLog("New spare key made");
        await NewSpareKeyAsync();
        SpareKeyNeeded?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private IReadOnlyList<NewKeyRequest> Requests => _state.NewKeyRequests ?? new List<NewKeyRequest>();

    private IEnumerable<NewKeyRequest> RecentRequests()
    {
        DateTimeOffset oldest = _time.GetUtcNow() - NewKeyRequestsKept;
        return Requests.Where(request => request.At > oldest);
    }

    /// <summary>Runs a check soon (on the UI thread).</summary>
    public void CheckSoon() => Dispatcher.UIThread.Post(() => _ = CheckAsync());

    private async Task CheckAsync()
    {
        if (_checking || !_control.IsAvailable)
        {
            return;
        }

        ColdTurkeyBlocks? blocks = _coldTurkey.Blocks;
        if (blocks is null)
        {
            return;
        }

        _checking = true;
        try
        {
            DateTime now = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime;
            if (_state.ReleasedOn != Today())
            {
                _state = _state with { Released = new List<string>(), ReleasedOn = Today() };
            }

            HoldWindows windows = Windows(now);
            // Password locks need a written-down spare key and Cold Turkey Pro; otherwise timed locks.
            bool passwordReady = HasSpareKey && blocks.IsPro;
            IReadOnlyDictionary<string, string> wanted = ColdTurkeyHold.Wanted(_state.Blocks, windows, passwordReady);
            // Blocks seen on outside the app's windows are what a day without a chosen list turns back on.
            var known = new HashSet<string>(_state.Known, StringComparer.Ordinal);
            known.UnionWith(blocks.Blocks.Where(b => b.IsSwitchedOn && !wanted.ContainsKey(b.Name)).Select(b => b.Name));

            DateTime lastRun = _state.LastRun is DateTimeOffset last
                ? TimeZoneInfo.ConvertTime(last, _time.LocalTimeZone).DateTime
                : now;
            if (lastRun < now.AddDays(-1))
            {
                lastRun = now.AddDays(-1);
            }

            IReadOnlyList<BlockRearm.Event> rearm = Array.Empty<BlockRearm.Event>();
            if (!_state.RearmOff && _deviceId() is Guid device && lastRun < now)
            {
                string me = BlockRef.Pc(device);
                rearm = BlockRearm.Between(_plan(), me, lastRun, now);
                foreach (BlockRearm.Event warning in BlockRearm.WarningsBetween(_plan(), me, lastRun, now))
                {
                    List<string> off = (warning.Blocks?.Select(b => b.Id) ?? known.Where(name => warning.Except?.Any(b => b.Id == name) != true))
                        .Where(name => blocks.Blocks.Any(b => b.Name == name && !b.IsSwitchedOn)).ToList();
                    if (off.Count > 0 && warning.At > now)
                    {
                        Notice?.Invoke(this, ($"Blocks come back on at {warning.At:h:mm tt}", string.Join(", ", off)));
                    }
                }
            }

            var memory = new HoldMemory(_state.PasswordHeld.ToHashSet(StringComparer.Ordinal), _state.Released.ToHashSet(StringComparer.Ordinal), known);
            IReadOnlyList<ColdTurkeyAction> actions = ColdTurkeyHold.Plan(now, _state.Blocks, windows, blocks, memory, passwordReady, rearm);
            _state = _state with { Known = known.ToList(), LastRun = _time.GetUtcNow() };
            bool rotate = false;
            foreach (ColdTurkeyAction action in actions)
            {
                rotate |= await RunAsync(action);
            }

            if (actions.Count > 0)
            {
                _coldTurkey.RefreshNow();
            }

            await SaveAsync();
            if (rotate)
            {
                await RotateAsync(keepHeld: false);
            }
        }
        finally
        {
            _checking = false;
        }
    }

    /// <returns>True when the action says the spare key was used (a new key is needed).</returns>
    private async Task<bool> RunAsync(ColdTurkeyAction action)
    {
        string? password = Unprotect(_state.ProtectedPassword);
        string name = action.Block;
        switch (action.Kind)
        {
            case ColdTurkeyAction.LockTimed:
                if (await _control.StartAsync(name, ColdTurkeyHold.LockMinutes))
                {
                    AddLog($"Locked {name} for {ColdTurkeyHold.LockMinutes} min ({action.Why})", quiet: action.Why == "held");
                }

                return false;
            case ColdTurkeyAction.LockPassword when password is not null:
                if (await _control.StartAsync(name, password: password))
                {
                    _state = _state with { PasswordHeld = _state.PasswordHeld.Append(name).Distinct().ToList() };
                    AddLog($"Locked {name} with the key ({action.Why})");
                }

                return false;
            case ColdTurkeyAction.StopPassword when password is not null:
                await _control.StopAsync(name, password);
                _state = _state with { PasswordHeld = _state.PasswordHeld.Where(other => other != name).ToList() };
                AddLog($"Unlocked {name} ({action.Why})");
                return false;
            case ColdTurkeyAction.Start:
                await _control.StartAsync(name);
                if (action.Why != "window ended" && action.Why != "released for today")
                {
                    AddLog($"Turned {name} back on ({action.Why})");
                }

                return false;
            case ColdTurkeyAction.Stop:
                await _control.StopAsync(name);
                AddLog($"Turned {name} off ({action.Why})");
                return false;
            case ColdTurkeyAction.SpareKeyUsed:
                _state = _state with
                {
                    Released = _state.Released.Append(name).Distinct().ToList(),
                    PasswordHeld = _state.PasswordHeld.Where(other => other != name).ToList(),
                };
                AddLog($"Spare key used: {name} released for today");
                Notice?.Invoke(this, ("Spare key used", $"{name} is released for the rest of today. Write down your new spare key."));
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// A new key after a spare key was used: blocks still locked with the old one are unlocked (left on); until the
    /// new key is retyped they're held with timed locks.
    /// </summary>
    private async Task RotateAsync(bool keepHeld)
    {
        string? old = Unprotect(_state.ProtectedPassword);
        if (!keepHeld && old is not null)
        {
            foreach (string name in _state.PasswordHeld.ToList())
            {
                await _control.StopAsync(name, old);
                if (!_state.Released.Contains(name))
                {
                    await _control.StartAsync(name);
                }
            }
        }

        // A new key asked for (ADR 41) is moot: this one replaces it.
        if (_state.PendingNewKeyAt is not null)
        {
            AddLog("New spare key request replaced by the key made after the spare key was used");
        }

        _state = _state with { PasswordHeld = new List<string>(), ProtectedPassword = null, KeyConfirmedAt = null, PendingNewKeyAt = null };
        await NewSpareKeyAsync();
        _coldTurkey.RefreshNow();
        SpareKeyNeeded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A focus session the user started is running, breaks included (what the keys are held for).</summary>
    private bool InFocusSession =>
        _sessions.State.IsActive && _sessions.State.Mode is SessionMode.Manual or SessionMode.Pomodoro;

    private HoldWindows Windows(DateTime now)
    {
        SessionState session = _sessions.State;
        bool focus = InFocusSession;
        IReadOnlyCollection<string> sessionBlocks = focus && session.ProfileId is Guid profile
            ? _coldTurkey.ExpectedFor(profile).ToList()
            : Array.Empty<string>();
        IReadOnlyCollection<Guid> categories = _plan().PartAt(now) is PlannedPart part ? new[] { part.Category.Id } : Array.Empty<Guid>();
        return new HoldWindows(focus, sessionBlocks, _preWork(), _workHours(), categories);
    }

    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime);

    private void AddLog(string line, bool quiet = false)
    {
        if (quiet)
        {
            return;
        }

        DateTime now = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime;
        _state = _state with { Log = _state.Log.Prepend($"{now:h:mm tt} · {line}").Take(LogMax).ToList() };
    }

    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveAsync(DocumentKey, _state);
        }
        catch (SqliteException)
        {
            // Kept in memory; the next check saves again.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Spaces, dashes and case don't matter here (Cold Turkey itself wants the key exactly as written).
    private static bool Same(string typed, string key) =>
        string.Equals(Plain(typed), Plain(key), StringComparison.OrdinalIgnoreCase);

    private static string Plain(string value) =>
        new(value.Where(character => character is not (' ' or '-')).ToArray());

    private string? Protect(string value) => Convert.ToBase64String(_protector.Protect(value));

    private string? Unprotect(string? stored)
    {
        if (stored is null)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(Convert.FromBase64String(stored));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The saved state: assignments, the key (current and pending, DPAPI), what's held and released, the log, and the
    /// requests for a new key (ADR 41) with the wait under way.
    /// </summary>
    private sealed record KeysState(
        List<HeldBlock> Blocks,
        string? ProtectedPassword,
        string? ProtectedPending,
        DateTimeOffset? KeyConfirmedAt,
        List<string> PasswordHeld,
        DateOnly? ReleasedOn,
        List<string> Released,
        List<string> Known,
        DateTimeOffset? LastRun,
        List<string> Log,
        bool RearmOff = false,
        List<NewKeyRequest>? NewKeyRequests = null,
        DateTimeOffset? PendingNewKeyAt = null)
    {
        public static KeysState Empty { get; } = new(new(), null, null, null, new(), null, new(), new(), null, new());
    }

    /// <summary>ADR 41: a request for a new spare key, its reason DPAPI-encrypted, kept 90 days; MadeAt once the key came.</summary>
    private sealed record NewKeyRequest(DateTimeOffset At, string? ProtectedReason, DateTimeOffset? MadeAt);
}
