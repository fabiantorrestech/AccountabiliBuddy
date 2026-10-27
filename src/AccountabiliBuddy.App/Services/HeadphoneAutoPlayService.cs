using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Plays the noise player's last file when chosen headphones connect (any time of day) and pauses it when they
/// disconnect, so it never carries on through the speakers. The watcher runs only while a switch is on.
/// </summary>
public sealed class HeadphoneAutoPlayService : IDisposable
{
    private const string DocumentKey = "headphones";
    // Windows moves sound to new headphones a moment after they appear.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);

    private readonly SqliteJsonDocumentStore _store;
    private readonly IHeadphoneWatcher _watcher;
    private readonly NoisePlayerService _noise;
    private readonly TimeProvider _time;
    private bool _watching;

    public HeadphoneAutoPlayService(SqliteJsonDocumentStore store, IHeadphoneWatcher watcher, NoisePlayerService noise, TimeProvider time)
    {
        _store = store;
        _watcher = watcher;
        _noise = noise;
        _time = time;
        _watcher.Changed += (_, change) => Dispatcher.UIThread.Post(() => _ = HandleAsync(change));
    }

    /// <summary>Today's day plan, for the "during a day plan window" option. Set by the app.</summary>
    public Func<DayPlan>? PlanProvider { get; set; }

    /// <summary>Counted focus on a date (Budget days). Set by the app.</summary>
    public Func<DateOnly, TimeSpan>? FocusedOn { get; set; }

    /// <summary>The night before a date from the sleep settings. Set by the app.</summary>
    public Func<DateOnly, SleepTimes?>? NightFallback { get; set; }

    /// <summary>The plan's categories, for the settings picker.</summary>
    public IReadOnlyList<DayCategory> PlanCategories => (PlanProvider?.Invoke() ?? DayPlan.Default).Categories;

    public HeadphoneSettings Current { get; private set; } = HeadphoneSettings.Default;

    public void LoadBlocking()
    {
        try
        {
            HeadphoneSettings? loaded = Task.Run(() => _store.LoadAsync<HeadphoneSettings>(DocumentKey)).GetAwaiter().GetResult();
            Current = loaded is null ? HeadphoneSettings.Default : loaded with { Devices = loaded.Devices ?? [] };
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = HeadphoneSettings.Default;
        }

        WatchIfOn();
    }

    public async Task SaveAsync(HeadphoneSettings settings)
    {
        Current = settings;
        WatchIfOn();
        try
        {
            await _store.SaveAsync(DocumentKey, settings);
        }
        catch (SqliteException)
        {
            // Applies for now; written with the next change.
        }
    }

    public Task<IReadOnlyList<BluetoothAudioDevice>> ListBluetoothAsync() => _watcher.ListBluetoothAudioAsync();

    public void Dispose() => _watcher.Dispose();

    private void WatchIfOn()
    {
        if (!_watching && Current.IsOn)
        {
            _watching = true;
            _watcher.Start();
        }
    }

    private async Task HandleAsync(HeadphoneEvent change)
    {
        bool chosen = change.IsBluetooth
            ? Current.Bluetooth && Current.Devices.Any(device => device.Id == change.DeviceId)
            : Current.Wired;
        if (!chosen)
        {
            return;
        }

        if (change.Connected)
        {
            if (!SchedulePermitsNow())
            {
                return;
            }

            await Task.Delay(SettleDelay);
            // The noise player's headphone lock still applies inside PlayNow.
            _noise.PlayNow();
        }
        else
        {
            _noise.PauseNow();
        }
    }

    private bool SchedulePermitsNow()
    {
        DayPlan plan = PlanProvider?.Invoke() ?? DayPlan.Default;
        // The app's time (trusted clock, its time zone), never the system's DateTime.Now.
        DateTime now = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime;
        return HeadphoneSchedule.AllowsNow(Current, plan, now, FocusedOn, NightFallback);
    }
}
