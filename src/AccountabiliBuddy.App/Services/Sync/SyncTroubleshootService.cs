using AccountabiliBuddy.App.Services.Backup;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The actions behind Settings › Devices &amp; sync › Troubleshoot sync (SYNC1b, ADR 86): the confirmation board, "Re-read everything",
/// "Send my files again", "Re-read goal history", "Try again" / "Ignore" / "Remove my stale file" on an unreadable file, and the diagnostics
/// report. Display and maintenance only: nothing here changes a setting or applies a looser one (everything still goes through the merge
/// rules and their waits), so nothing needs a lock in a focus session; "Ignore" only hides a line and never touches
/// <see cref="ReceivedDevicesService.LastProblem"/>. Each action returns one result line made of counts only. Use from the UI thread.
/// </summary>
public sealed class SyncTroubleshootService
{
    /// <summary>The relay is re-read at most this often (its quota is shared by every device on one address).</summary>
    public static readonly TimeSpan RelayRereadEvery = TimeSpan.FromMinutes(5);

    /// <summary>"Send my files again" runs at most this often.</summary>
    public static readonly TimeSpan SendAgainEvery = TimeSpan.FromMinutes(1);

    // A folder is read at the user's interval, but a phone's folder pass is the system's to schedule: never expect it sooner than this.
    private static readonly TimeSpan FolderExpectedAtLeast = TimeSpan.FromMinutes(15);

    private readonly SyncCoordinator _coordinator;
    private readonly SyncIdentityService _identity;
    private readonly SyncSettingsService _settings;
    private readonly ReceivedDevicesService _received;
    private readonly SyncFolderService _folder;
    private readonly SyncDriveService _drive;
    private readonly SyncRelayService _relay;
    private readonly TimeProvider _time;
    private bool _busy;
    private DateTimeOffset _lastRelayReread = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSendAgain = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRelayHistory = DateTimeOffset.MinValue;

    public SyncTroubleshootService(
        SyncCoordinator coordinator, SyncIdentityService identity, SyncSettingsService settings, ReceivedDevicesService received,
        SyncFolderService folder, SyncDriveService drive, SyncRelayService relay, TimeProvider time)
    {
        _coordinator = coordinator;
        _identity = identity;
        _settings = settings;
        _received = received;
        _folder = folder;
        _drive = drive;
        _relay = relay;
        _time = time;
    }

    /// <summary>Spaces the relay's requests for a re-read; a fake in checks.</summary>
    public Func<RelayRequestPacer> PacerFactory { get; set; } = () => new RelayRequestPacer(static () => DateTimeOffset.UtcNow, static (span, token) => Task.Delay(span, token));

    /// <summary>The save-as folder check needs the sync folder; the caller may swap what "the sync folder" is in checks.</summary>
    public Func<string?> SyncFolder { get; set; } = () => null;

    /// <summary>True while an action runs (the buttons are off meanwhile).</summary>
    public bool IsBusy => _busy;

    /// <summary>The unreadable and leftover files this PC found (the list's source).</summary>
    public SyncUnreadableFiles? Files => _coordinator.Unreadable;

    /// <summary>
    /// A forced goal history pass on the relay (Re-read goal history, and the history part of Send my files again) may start: the relay's quota is
    /// shared by every device on one address, so these passes run at most once per <see cref="RelayRereadEvery"/>. True when the relay is off.
    /// </summary>
    public bool RelayHistoryOpen => !_settings.Current.RelayOn || _time.GetUtcNow() - _lastRelayHistory >= RelayRereadEvery;

    /// <summary>The device numbering ("PC 1", "Phone 1") the board, the unreadable list, the report and the log share: every device this PC holds a file of, removed ones too.</summary>
    public IReadOnlyDictionary<Guid, string> Ordinals() => SyncBoardBuilder.Ordinals(_received.Devices.Select(device => device.File.Device));

    /// <summary>The confirmation board as of now.</summary>
    public SyncBoard BuildBoard()
    {
        if (_coordinator.SendLog is not { } sent || _coordinator.Receipts is not { } receipts)
        {
            return new SyncBoard([], null);
        }

        return SyncBoardBuilder.Build(_time.GetUtcNow(), _identity.DeviceId, _coordinator.OwnMethods(), _received.Active, receipts.Value, sent, ExpectedInterval, Ordinals());
    }

    /// <summary>
    /// How often a receiver is expected to write when the method delivers by itself: the caller's real setting for the method. The folder's is the
    /// user's interval (never under 15 minutes, a phone's folder pass is the system's to schedule), Drive's and the relay's the 5 minutes of the idle
    /// poll and the heartbeat; null for QR and file import (by hand, never "quiet").
    /// </summary>
    public TimeSpan? ExpectedInterval(string method)
    {
        switch (method)
        {
            case SyncReceiptParts.Folder:
                TimeSpan chosen = TimeSpan.FromMinutes(_settings.Current.Interval switch
                {
                    SyncInterval.FiveMinutes => 5,
                    SyncInterval.FifteenMinutes => 15,
                    _ => 1,
                });
                return chosen > FolderExpectedAtLeast ? chosen : FolderExpectedAtLeast;
            case SyncReceiptParts.Drive:
            case SyncReceiptParts.Relay:
                return TimeSpan.FromMinutes(5);
            default:
                return null;
        }
    }

    /// <summary>
    /// "Re-read everything received": the "seen" marks go and every file of each method that is on is read and merged again (merging is
    /// idempotent). The relay is paced and capped (<see cref="RelayRequestPacer"/>, never alongside a goal history pass, once per
    /// <see cref="RelayRereadEvery"/>). Returns the result line.
    /// </summary>
    public async Task<string> RereadAllAsync(CancellationToken cancellationToken = default)
    {
        if (_busy)
        {
            return "Already working.";
        }

        _busy = true;
        try
        {
            var results = new List<SyncRereadResult>();
            string? relayNote = null;
            SyncSettings settings = _settings.Current;
            if (_identity.Key is null)
            {
                return "Not syncing: this PC isn't in a device group.";
            }

            if (settings.FolderOn)
            {
                results.Add(await _folder.RereadAllAsync());
            }

            if (settings.DriveOn)
            {
                results.Add(await _drive.RereadAllAsync());
            }

            if (settings.RelayOn)
            {
                DateTimeOffset now = _time.GetUtcNow();
                if (now - _lastRelayReread < RelayRereadEvery)
                {
                    relayNote = "The relay was re-read a moment ago; it can be re-read again in " + Math.Max(1, (int)Math.Ceiling((RelayRereadEvery - (now - _lastRelayReread)).TotalMinutes)) + " min.";
                }
                else
                {
                    _lastRelayReread = now;
                    results.Add(await _relay.RereadAllAsync(PacerFactory(), cancellationToken));
                }
            }

            _coordinator.SyncLogger?.Note("reread");
            if (results.Count == 0 && relayNote is null)
            {
                return "No sync method is on: nothing to re-read.";
            }

            int files = results.Sum(result => result.Files);
            int problems = results.Sum(result => result.Problems);
            string line = $"Re-read {files} {(files == 1 ? "file" : "files")} · {problems} {(problems == 1 ? "problem" : "problems")}";
            if (results.Any(result => result.Stopped))
            {
                line += " · stopped early: the relay said too many requests";
            }

            return relayNote is null ? line : line + " · " + relayNote;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// "Send my files again now": this PC's sync file is written and put on every method that is on, and its goal history files are marked to be
    /// put again and a normal pass runs (the history rules stay: only kept months, the relay's quota and request cap). At most once a minute.
    /// </summary>
    public async Task<string> SendAgainAsync()
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (_busy)
        {
            return "Already working.";
        }

        if (now - _lastSendAgain < SendAgainEvery)
        {
            return "Sent a moment ago. Try again in a minute.";
        }

        _busy = true;
        _lastSendAgain = now;
        try
        {
            SyncSettings settings = _settings.Current;
            if (_identity.Key is null)
            {
                return "Not syncing: this PC isn't in a device group.";
            }

            int sent = 0;
            int failed = 0;
            if (settings.FolderOn)
            {
                if (await _folder.WriteNowAsync())
                {
                    sent++;
                }
                else
                {
                    failed++;
                }
            }

            if (settings.DriveOn)
            {
                if (await _drive.WriteNowAsync())
                {
                    sent++;
                }
                else
                {
                    failed++;
                }
            }

            if (settings.RelayOn)
            {
                if (await _relay.WriteNowAsync())
                {
                    sent++;
                }
                else
                {
                    failed++;
                }
            }

            bool relayHistoryWaits = false;
            if (settings.ShareTaskLists)
            {
                relayHistoryWaits = await ResendHistoryAsync(settings);
            }

            _coordinator.SyncLogger?.Note("resend");
            string history = !settings.ShareTaskLists
                ? string.Empty
                : relayHistoryWaits ? " · relay goal history can go again in a few minutes" : " · goal history goes by its normal pass";
            return sent + failed == 0
                ? "No sync method is on: nothing to send."
                : $"Sent to {sent} {(sent == 1 ? "place" : "places")} · {failed} failed" + history;
        }
        finally
        {
            _busy = false;
        }
    }

    // Returns true when the relay's part was left for later (the 5-minute gate).
    private async Task<bool> ResendHistoryAsync(SyncSettings settings)
    {
        if (settings.DriveOn && _drive.GoalHistory is { } driveHistory)
        {
            await driveHistory.ResendKeptAsync("drive");
            await _drive.HistoryAsync(force: true);
        }

        bool waits = false;
        if (settings.RelayOn && _relay.GoalHistory is { } relayHistory)
        {
            if (RelayHistoryOpen)
            {
                _lastRelayHistory = _time.GetUtcNow();
                await relayHistory.ResendKeptAsync("relay");
                await _relay.HistoryAsync(force: true);
            }
            else
            {
                waits = true;
            }
        }

        _coordinator.SyncLogger?.Note("history-resend");
        return waits;
    }

    /// <summary>
    /// "Re-read goal history (full)": this PC is new to the group again on Drive and the relay (every listed history file is read once through the
    /// normal pass: its request cap, the relay's quota and the "no download before a goals part merged" gate all stay), then a pass runs. A relay
    /// pass stops at its cap and goes on at the next one.
    /// </summary>
    public async Task<string> RereadHistoryAsync()
    {
        if (_busy)
        {
            return "Already working.";
        }

        _busy = true;
        try
        {
            SyncSettings settings = _settings.Current;
            if (_identity.Key is null || !settings.ShareTaskLists || (!settings.DriveOn && !settings.RelayOn))
            {
                return "Goal history travels by Google Drive or the relay, with task lists shared: none is on.";
            }

            if (settings.DriveOn && _drive.GoalHistory is { } driveHistory)
            {
                await driveHistory.ForceRereadAsync("drive");
                await _drive.HistoryAsync(force: true);
            }

            bool relayWaits = false;
            if (settings.RelayOn && _relay.GoalHistory is { } relayHistory)
            {
                if (RelayHistoryOpen)
                {
                    _lastRelayHistory = _time.GetUtcNow();
                    await relayHistory.ForceRereadAsync("relay");
                    await _relay.HistoryAsync(force: true);
                }
                else
                {
                    relayWaits = true;
                }
            }

            if (relayWaits && !settings.DriveOn)
            {
                return "Wait a few minutes: the relay was read a moment ago.";
            }

            _coordinator.SyncLogger?.Note("history-reread");
            return relayWaits
                ? "Goal history read again on Google Drive · the relay can be read again in a few minutes"
                : "Goal history read again · a big history continues in the next pass";
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>"Try again" on one unreadable file: its "seen" mark goes and its method reads now.</summary>
    public async Task<string> TryAgainAsync(SyncUnreadableFile file)
    {
        switch (file.Method)
        {
            case SyncReceiptParts.Folder:
                _folder.ForgetSeen(file.Name);
                await _folder.ReadNowAsync();
                break;
            case SyncReceiptParts.Drive:
                _drive.ForgetSeen(file.Name);
                await _drive.ReadNowAsync();
                break;
            case SyncReceiptParts.Relay:
                _relay.ForgetSeen(file.Name);
                await _relay.ReadNowAsync();
                break;
            default:
                return "That file can only be imported again by hand.";
        }

        _coordinator.SyncLogger?.Note("retry");
        return Files?.Find(file.Method, file.Key) is null ? "Read it now: it's fine." : "Still can't be read.";
    }

    /// <summary>"Ignore": hides the line. Nothing else changes.</summary>
    public void Ignore(SyncUnreadableFile file)
    {
        Files?.Ignore(file.Method, file.Key);
        _coordinator.SyncLogger?.Note("ignored");
    }

    /// <summary>
    /// "Remove my stale file": only this PC's own old-group or extra copy, on the folder or Google Drive. Never on the relay (a relay name is unique
    /// per device and the relay deletes only when a device leaves, ADR 46), never another device's file and never this PC's current file.
    /// </summary>
    public async Task<string> RemoveStaleAsync(SyncUnreadableFile file)
    {
        if (!file.OwnStale)
        {
            return "That isn't this PC's own file.";
        }

        SyncRemoveOutcome outcome = file.Method switch
        {
            SyncReceiptParts.Folder => await _folder.RemoveOwnStaleAsync(file.Name),
            SyncReceiptParts.Drive => await _drive.RemoveOwnStaleAsync(file.Name),
            _ => SyncRemoveOutcome.Refused,
        };
        if (outcome == SyncRemoveOutcome.Removed)
        {
            _coordinator.SyncLogger?.Note("stale-removed");
        }

        return outcome switch
        {
            SyncRemoveOutcome.Removed => "Removed this PC's old copy.",
            SyncRemoveOutcome.AlreadyGone => "Already gone.",
            _ => "Couldn't remove it; try again later.",
        };
    }

    /// <summary>The diagnostics report's text (<see cref="SyncDiagnosticsExport"/>).</summary>
    public string BuildReport()
    {
        SyncBoard board = BuildBoard();
        IReadOnlyDictionary<Guid, string> ordinals = Ordinals();
        return SyncDiagnosticsExport.Build(
            _time.GetUtcNow(), _time.LocalTimeZone, BackupService.AppVersion, board, Files?.All ?? [], ordinals, _coordinator.SyncLogger?.Lines() ?? []);
    }

    /// <summary>Saves the report at <paramref name="path"/>, unless it is inside the sync folder. Returns one line.</summary>
    public async Task<string> ExportAsync(string path)
    {
        if (SyncDiagnosticsExport.RefusalFor(path, _settings.Current.FolderPath ?? SyncFolder()) is { } refusal)
        {
            return refusal;
        }

        try
        {
            await File.WriteAllTextAsync(path, BuildReport());
            return "Saved the report. It stays where you put it; nothing was sent anywhere.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "Couldn't save the report there.";
        }
    }
}
