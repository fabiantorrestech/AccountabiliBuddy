using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The guidance mode (ADR 68): AccountabiliBuddy (guarding) or ProductaBuddy (companion only), one choice for every linked device.
/// App-owned document "guidance" (<see cref="GuidanceState"/>). Switching to ProductaBuddy waits the looser-change delay and applies
/// only with no session running (<see cref="TickAsync"/>). During a session it is "after this session ends", or "now" once what
/// ending the session early costs is paid (<see cref="RequestCompanionNowAsync"/>; the reason is kept sealed, document
/// "guidance.reasons", 90 days). Back to AccountabiliBuddy is at once. The chosen mode goes in the sync file and another device's
/// newer choice is taken (<see cref="Receive"/>). A fresh install starts in ProductaBuddy, an upgraded one in AccountabiliBuddy,
/// both as defaults nobody chose. Use from the UI thread only.
/// </summary>
public sealed class GuidanceModeService
{
    private const string DocumentKey = "guidance";

    // The setup assistant's mode page was answered on this PC (kept apart from the synced state: it is only about the page).
    private const string AskedKey = "guidance.asked";

    // Why ProductaBuddy was chosen "now" during a session: sealed, never synced, kept as long as the activity history.
    private const string ReasonsKey = "guidance.reasons";
    private static readonly TimeSpan KeepReasonsFor = TimeSpan.FromDays(90);

    private readonly SqliteJsonDocumentStore _store;
    private readonly SessionController _sessions;
    private readonly Func<TimeSpan> _looserWait;
    private readonly Core.Ports.IDataProtector _protector;

    public GuidanceModeService(SqliteJsonDocumentStore store, SessionController sessions, Func<TimeSpan> looserWait, Core.Ports.IDataProtector protector)
    {
        _store = store;
        _sessions = sessions;
        _looserWait = looserWait;
        _protector = protector;
    }

    public GuidanceState State { get; private set; } = GuidanceState.Upgraded;

    /// <summary>ProductaBuddy is in effect: no watching, escalation, work-hours guard, Pre-Work, start nudge or bedtime reminders.</summary>
    public bool IsCompanion => State.Mode == GuidanceMode.Companion;

    /// <summary>When a switch to ProductaBuddy applies, while one waits; null otherwise.</summary>
    public DateTimeOffset? PendingAt => State.Pending?.ApplyAt;

    /// <summary>Raised when the mode or a waiting switch changes (gates, the brand name, the sync file and the waits follow).</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The mode was chosen here or on another device, a switch waits, or the setup assistant's mode page was answered (keeping the
    /// mode shown): what the migration assistant counts as set up.
    /// </summary>
    public bool IsSetUp => State.IsChosen || State.Pending is not null || _asked;

    private bool _asked;

    public void LoadBlocking()
    {
        try
        {
            GuidanceState? stored = Task.Run(() => _store.LoadAsync<GuidanceState>(DocumentKey)).GetAwaiter().GetResult();
            // An upgraded default saved before it carried UpgradeStamp (a guarding mode nobody chose) gets it now, so it is sent.
            State = stored is { Mode: GuidanceMode.Guarding, EditedAt: null } ? stored with { EditedAt = GuidanceState.UpgradeStamp } : stored ?? WithoutSavedDocument();
            _asked = Task.Run(() => _store.LoadAsync<AskedRecord>(AskedKey)).GetAwaiter().GetResult() is not null;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            State = GuidanceState.Upgraded;
        }

        AppBrand.Set(State.Mode);
    }

    /// <summary>The setup assistant's mode page was answered with the mode shown (nothing changes, nothing is sent).</summary>
    public async Task MarkAskedAsync()
    {
        if (_asked)
        {
            return;
        }

        _asked = true;
        await _store.SaveAsync(AskedKey, new AskedRecord(_sessions.Now));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Asks for ProductaBuddy: it applies after the looser-change delay.</summary>
    /// <returns>False when refused (a focus session runs).</returns>
    public async Task<bool> RequestCompanionAsync()
    {
        GuidanceState? next = GuidanceRules.RequestCompanion(State, _sessions.Now, _looserWait(), _sessions.State.IsActive);
        if (next is null)
        {
            return false;
        }

        await SaveAsync(next);
        return true;
    }

    /// <summary>During a session, "after this session ends": the looser-change wait, then no session running.</summary>
    public Task RequestCompanionAfterSessionAsync()
    {
        return SaveAsync(GuidanceRules.RequestCompanionAfterSession(State, _sessions.Now, _looserWait()));
    }

    /// <summary>
    /// During a session, "now", once ending the session early's wait was waited and <paramref name="reason"/> typed (the caller
    /// checked both): the looser-change wait starts now and the switch applies with the session still running.
    /// </summary>
    public async Task RequestCompanionNowAsync(string reason)
    {
        DateTimeOffset now = _sessions.Now;
        await KeepReasonAsync(now, reason);
        await SaveAsync(GuidanceRules.RequestCompanionNow(State, now, _looserWait()));
    }

    /// <summary>"Keep" on the setup page: the starting default becomes a choice and is sent.</summary>
    public Task ConfirmDefaultAsync() => SaveAsync(GuidanceRules.ConfirmDefault(State, _sessions.Now));

    /// <summary>AccountabiliBuddy at once, cancelling a waiting switch (stricter, never refused).</summary>
    public Task ChooseGuardingAsync()
    {
        return SaveAsync(GuidanceRules.ChooseGuarding(_sessions.Now));
    }

    /// <summary>Applies a waiting switch once due and no session runs. Runs every second.</summary>
    public Task TickAsync()
    {
        return SaveAsync(GuidanceRules.ApplyIfDue(State, _sessions.Now, _sessions.State.IsActive));
    }

    /// <summary>The chosen mode for the sync file; null for a default nobody chose.</summary>
    public SyncGuidance? ForSync() => GuidanceRules.ForSync(State);

    /// <summary>Takes another device's newer choice: AccountabiliBuddy at once, ProductaBuddy after this PC's delay from its edit time.</summary>
    public void Receive(SyncGuidance received)
    {
        _ = SaveAsync(GuidanceRules.Receive(State, received, _sessions.Now, _looserWait()));
    }

    private async Task SaveAsync(GuidanceState next)
    {
        if (next == State)
        {
            return;
        }

        State = next;
        AppBrand.Set(next.Mode);
        Changed?.Invoke(this, EventArgs.Empty);
        // Writes go one after another, so a quick second change (a sync arriving as you choose) never lands on disk before the first.
        _lastSave = SaveAfterAsync(_lastSave, next);
        await _lastSave;
    }

    private Task _lastSave = Task.CompletedTask;

    private async Task SaveAfterAsync(Task previous, GuidanceState state)
    {
        try
        {
            await previous;
        }
        catch (SqliteException)
        {
            // The earlier write failed; this newer state is written anyway.
        }

        await _store.SaveAsync(DocumentKey, state);
    }

    /// <summary>
    /// No saved mode. An upgrade keeps guarding: the data ran an older version before ("app.version" has a Previous), or the setup
    /// assistant was shown before (data older than the version record). A fresh install starts in ProductaBuddy. Either is saved
    /// once, so a later upgrade doesn't mistake this install for an older one.
    /// </summary>
    private GuidanceState WithoutSavedDocument()
    {
        AppVersionRecord? version = Task.Run(() => _store.LoadAsync<AppVersionRecord>("app.version")).GetAwaiter().GetResult();
        bool assistantShown = Task.Run(() => _store.LoadAsync<SetupAssistantRecord>("setup.assistant")).GetAwaiter().GetResult() is not null;
        GuidanceState state = version?.Previous is not null || assistantShown ? GuidanceState.Upgraded : GuidanceState.FreshInstall;
        Task.Run(() => _store.SaveAsync(DocumentKey, state)).GetAwaiter().GetResult();
        return state;
    }

    private async Task KeepReasonAsync(DateTimeOffset now, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return; // A session with no set end costs no reason, as ending it doesn't.
        }

        try
        {
            List<SealedReason> kept = (await _store.LoadAsync<List<SealedReason>>(ReasonsKey) ?? [])
                .Where(entry => entry.At > now - KeepReasonsFor)
                .ToList();
            kept.Add(new SealedReason(now, Convert.ToBase64String(_protector.Protect(reason.Trim()))));
            await _store.SaveAsync(ReasonsKey, kept);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // The switch still happens: the wait was paid; only the note of why is lost.
        }
    }

    private sealed record AskedRecord(DateTimeOffset At);

    private sealed record SealedReason(DateTimeOffset At, string Sealed);
}
