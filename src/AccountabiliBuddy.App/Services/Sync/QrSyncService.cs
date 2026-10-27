using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using QRCoder;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The QR code method (ADR 22): today's part of this PC's sync file as up to four codes the phone scans
/// (<see cref="SyncFileBuilder.ForQr"/>), and an end-of-day reminder to scan them. The reminder is due at
/// <see cref="SyncSettings.ReminderAt"/>, else when the day plan's last Work part ends; it comes once a day, "Later"
/// puts it off, and it doesn't come when every phone already has this PC's latest change. Use from the UI thread only.
/// </summary>
public sealed class QrSyncService : IDisposable
{
    private const string DocumentKey = "sync.qrReminder";
    private const int PixelsPerModule = 4;
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    private readonly SyncCoordinator _coordinator;
    private readonly SyncIdentityService _identity;
    private readonly SyncSettingsService _settings;
    private readonly ReceivedDevicesService _received;
    private readonly ConfigurationService _configuration;
    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;
    private ReminderState _state = new(null, null);
    private DispatcherTimer? _timer;

    // When this PC's own data last changed; not kept across restarts, so a new run starts as "changed".
    private DateTimeOffset _lastOwnChange;

    public QrSyncService(
        SyncCoordinator coordinator,
        SyncIdentityService identity,
        SyncSettingsService settings,
        ReceivedDevicesService received,
        ConfigurationService configuration,
        SqliteJsonDocumentStore store,
        TimeProvider time)
    {
        _coordinator = coordinator;
        _identity = identity;
        _settings = settings;
        _received = received;
        _configuration = configuration;
        _store = store;
        _time = time;
        _lastOwnChange = time.GetUtcNow();
        coordinator.OwnDataChanged += (_, _) => NoteOwnChange(_time.GetUtcNow());
    }

    /// <summary>Counted focus on a date, for the day plan's Budget days (a met budget ends the day's work windows). Set by the app.</summary>
    public Func<DateOnly, TimeSpan>? FocusedOn { get; set; }

    /// <summary>The reminder is due: show "Scan today's code to sync your phone" with Show code and Later.</summary>
    public event EventHandler? ReminderDue;

    public void LoadBlocking()
    {
        try
        {
            _state = Task.Run(() => _store.LoadAsync<ReminderState>(DocumentKey)).GetAwaiter().GetResult() ?? _state;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // A reminder state that can't be read starts over: at worst the reminder comes once more today.
        }
    }

    /// <summary>Checks every minute whether the reminder is due.</summary>
    public void Start()
    {
        _timer = new DispatcherTimer { Interval = CheckEvery };
        _timer.Tick += (_, _) => _ = ShowReminderIfDueAsync(_time.GetUtcNow());
        _timer.Start();
    }

    /// <summary>The links of today's codes for <paramref name="file"/>: at most <see cref="SyncQr.MaxCodes"/>.</summary>
    public static IReadOnlyList<string> LinksFor(SyncFile file, SyncKey key, TimeZoneInfo zone)
    {
        return SyncQr.Links(SyncFileCodec.Seal(SyncFileBuilder.ForQr(file, zone), key), key.GroupId);
    }

    /// <summary>The links of this PC's codes as of now; none while it isn't in a device group.</summary>
    /// <exception cref="SqliteException">The history can't be read right now.</exception>
    public async Task<IReadOnlyList<string>> LinksForNowAsync()
    {
        if (_identity.Key is not SyncKey key)
        {
            return [];
        }

        SyncFile own = await _coordinator.BuildOwnFileAsync();
        return LinksFor(own, key, _time.LocalTimeZone);
    }

    /// <summary>The links as QR code pictures, in order.</summary>
    public static IReadOnlyList<Bitmap> Render(IReadOnlyList<string> links)
    {
        return links.Select(link =>
        {
            using var generator = new QRCodeGenerator();
            using QRCodeData data = generator.CreateQrCode(link, QRCodeGenerator.ECCLevel.M);
            return new Bitmap(new MemoryStream(new PngByteQRCode(data).GetGraphic(PixelsPerModule)));
        }).ToList();
    }

    /// <summary>This PC's own data changed at <paramref name="at"/> (a phone that saw it before needs it again).</summary>
    public void NoteOwnChange(DateTimeOffset at)
    {
        _lastOwnChange = at;
    }

    /// <summary>When the reminder is due on <paramref name="now"/>'s day; null when that day has no Work part and no time is set.</summary>
    public DateTimeOffset? DueAt(DateTimeOffset now)
    {
        TimeZoneInfo zone = _time.LocalTimeZone;
        DateTime day = TimeZoneInfo.ConvertTime(now, zone).Date;
        if (_settings.Current.ReminderAt is TimeOnly at)
        {
            DateTime local = day + at.ToTimeSpan();
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }

        DateTime? lastWorkEnd = DayWorkWindows.WorkWindows(_configuration.Current.DayPlan, new LocalWindow(day, day.AddDays(1)), TimeZoneInfo.ConvertTime(now, zone).DateTime, FocusedOn)
            .Select(focus => (DateTime?)focus.Window.End)
            .Max();
        return lastWorkEnd is DateTime end ? new DateTimeOffset(end, zone.GetUtcOffset(end)) : null;
    }

    /// <summary>Raises <see cref="ReminderDue"/> when the reminder is due at <paramref name="now"/>, and notes it.</summary>
    public async Task ShowReminderIfDueAsync(DateTimeOffset now)
    {
        if (!_settings.Current.QrOn || _identity.Key is null)
        {
            return;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _time.LocalTimeZone).Date);
        if (_state.SnoozedUntil is DateTimeOffset until)
        {
            if (now < until)
            {
                return;
            }
        }
        else if (_state.LastShownDay == today || DueAt(now) is not DateTimeOffset due || now < due)
        {
            return;
        }

        if (EveryPhoneHasLatest())
        {
            return;
        }

        await SaveAsync(new ReminderState(today, null));
        ReminderDue?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>"Later": the reminder comes back after <paramref name="minutes"/>, or <see cref="SyncSettings.SnoozeMinutes"/>.</summary>
    public Task SnoozeAsync(int? minutes)
    {
        TimeSpan wait = TimeSpan.FromMinutes(minutes ?? _settings.Current.SnoozeMinutes);
        return SaveAsync(_state with { SnoozedUntil = _time.GetUtcNow() + wait });
    }

    public void Dispose()
    {
        _timer?.Stop();
    }

    // Every phone this PC has a file from holds this PC's file from after its last change. No phone yet: it hasn't.
    private bool EveryPhoneHasLatest()
    {
        List<ReceivedDevice> phones = _received.Devices.Where(device => device.File.Device.Kind == SyncDeviceCard.KindPhone).ToList();
        return phones.Count > 0 && phones.All(phone =>
            phone.File.Device.Seen.TryGetValue(_identity.DeviceId, out DateTimeOffset seen) && seen >= _lastOwnChange);
    }

    private async Task SaveAsync(ReminderState state)
    {
        _state = state;
        try
        {
            await _store.SaveAsync(DocumentKey, state);
        }
        catch (SqliteException)
        {
            // Kept in memory; at worst the reminder comes once more after a restart.
        }
    }

    /// <param name="LastShownDay">The local day the reminder last came.</param>
    /// <param name="SnoozedUntil">Put off until then by "Later"; null when not snoozed.</param>
    private sealed record ReminderState(DateOnly? LastShownDay, DateTimeOffset? SnoozedUntil);
}
