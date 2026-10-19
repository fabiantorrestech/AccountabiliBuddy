using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Keeps the <see cref="TrustedClock"/>'s anchor on disk (documents key "clock.anchor"), so a Windows clock changed
/// while the app was closed is still ignored after a restart in the same boot. Saved at launch, when a clock jump
/// starts or stops being ignored, and every 10 minutes. Use from the UI thread only.
/// </summary>
public sealed class ClockAnchorService : IDisposable
{
    private const string DocumentKey = "clock.anchor";
    private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(1);

    private readonly TrustedClock _clock;
    private readonly SqliteJsonDocumentStore _store;
    private readonly DispatcherTimer _timer;

    // Saves run one after another so an older anchor never overwrites a newer one.
    private Task _pendingSave = Task.CompletedTask;

    public ClockAnchorService(TrustedClock clock, SqliteJsonDocumentStore store)
    {
        _clock = clock;
        _store = store;
        _timer = new DispatcherTimer { Interval = SaveInterval };
        _timer.Tick += (_, _) => Save();
    }

    /// <summary>Loads the saved anchor into the clock. Call before anything else reads the time.</summary>
    public void RestoreBlocking()
    {
        try
        {
            ClockAnchor? saved = Task.Run(() => _store.LoadAsync<ClockAnchor>(DocumentKey)).GetAwaiter().GetResult();
            _clock.Restore(saved);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // Unreadable anchor: trust the Windows clock, as after a reboot.
        }
    }

    public void Start()
    {
        _clock.SystemClockOffsetChanged += OnOffsetChanged;
        Save();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _clock.SystemClockOffsetChanged -= OnOffsetChanged;
        Save();
        _pendingSave.Wait(FlushTimeout);
    }

    private void OnOffsetChanged(object? sender, TimeSpan offset)
    {
        // May be raised on any thread that read the time.
        Dispatcher.UIThread.Post(Save);
    }

    private void Save()
    {
        ClockAnchor? anchor = _clock.CreateAnchor();
        if (anchor is null)
        {
            return;
        }

        _pendingSave = _pendingSave.ContinueWith(
            async _ =>
            {
                try
                {
                    await _store.SaveAsync(DocumentKey, anchor);
                }
                catch (SqliteException)
                {
                    // Best effort: the next save tries again.
                }
            },
            TaskScheduler.Default).Unwrap();
    }
}
