using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The daily step counts a paired phone read from Health Connect and shares in its sealed sync file (<see cref="SyncFile.Steps"/>).
/// This PC never reads Health Connect and opens no connection: it only shows what the file carries, and only once a phone's file
/// carries steps. The card can be hidden here (document "dailySteps", this PC only). Use from the UI thread only.
/// </summary>
public sealed class DailyStepsService
{
    private const string DocumentKey = "dailySteps";

    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;
    private SyncSteps? _latest;
    private DailyStepsSettings _settings = new();

    public DailyStepsService(SqliteJsonDocumentStore store, TimeProvider time)
    {
        _store = store;
        _time = time;
    }

    /// <summary>The newest phone's counts; null while no phone's file carries steps (the card then doesn't show).</summary>
    public SyncSteps? Current => _latest is { } steps && PhoneSteps.IsFresh(steps, _time.GetUtcNow()) ? steps : null;

    /// <summary>The user hid the card on this PC.</summary>
    public bool Hidden => _settings.Hidden;

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            _settings = Task.Run(() => _store.LoadAsync<DailyStepsSettings>(DocumentKey)).GetAwaiter().GetResult() ?? new DailyStepsSettings();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _settings = new DailyStepsSettings();
        }
    }

    /// <summary>Takes the counts of the newest phone file that has them (null when none does); raises <see cref="Changed"/> when they differ.</summary>
    public void Follow(IReadOnlyList<ReceivedDevice> devices)
    {
        SyncSteps? steps = PhoneSteps.Of(devices, _time.GetUtcNow());
        if (steps == _latest || (steps is not null && _latest is not null && SameSteps(steps, _latest)))
        {
            return;
        }

        _latest = steps;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetHiddenAsync(bool hidden)
    {
        if (hidden == _settings.Hidden)
        {
            return;
        }

        _settings = _settings with { Hidden = hidden };
        Changed?.Invoke(this, EventArgs.Empty);
        await _store.SaveAsync(DocumentKey, _settings);
    }

    /// <summary>The count of <paramref name="date"/>; null when the phone had none for that day (shown as no data, never as 0).</summary>
    public int? CountOn(DateOnly date) => Current?.Days.FirstOrDefault(day => day.Date == date)?.Count;

    // Records compare their lists by reference, so two reads of the same file differ: compare the content.
    private static bool SameSteps(SyncSteps a, SyncSteps b) =>
        a.Goal == b.Goal && a.ReadAt == b.ReadAt && a.Days.SequenceEqual(b.Days);
}
