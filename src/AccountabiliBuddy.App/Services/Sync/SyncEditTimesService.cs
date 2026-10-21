using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// When each shared setting (<see cref="SyncDocument.ProfilesKey"/>, <see cref="SyncDocument.SleepKey"/>,
/// <see cref="SyncDocument.DayBarKey"/>) was last edited on this PC, so the newest edit wins across devices (ADR 22).
/// Stamps the app's trusted time when the setting changes, except while a received change is applied
/// (<see cref="SuppressStamping"/>). A day-plan-only change isn't a profiles edit: the plan has its own edit time.
/// Kept in the documents table ("sync.editedAt"). Use from the UI thread only.
/// </summary>
public sealed class SyncEditTimesService
{
    private const string DocumentKey = "sync.editedAt";

    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;
    private Dictionary<string, DateTimeOffset> _editedAt = [];
    private string _profilesJson;
    private int _suppressed;

    // Saves run one after another so an older set of stamps never overwrites a newer one.
    private Task _saving = Task.CompletedTask;

    public SyncEditTimesService(
        ConfigurationService configuration, SleepSettingsService sleep, DayBarSettingsService dayBar, SqliteJsonDocumentStore store, TimeProvider time)
    {
        _store = store;
        _time = time;
        _profilesJson = WithoutDayPlan(configuration.Current);
        configuration.Changed += (_, current) => OnProfilesChanged(current);
        sleep.Changed += (_, _) => Stamp(SyncDocument.SleepKey);
        dayBar.Changed += (_, _) => Stamp(SyncDocument.DayBarKey);
    }

    public void LoadBlocking()
    {
        try
        {
            _editedAt = Task.Run(() => _store.LoadAsync<Dictionary<string, DateTimeOffset>>(DocumentKey)).GetAwaiter().GetResult() ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _editedAt = [];
        }
    }

    /// <returns>When the setting was last edited here; null when it hasn't been since this PC started keeping track.</returns>
    public DateTimeOffset? EditedAt(string key)
    {
        return _editedAt.TryGetValue(key, out DateTimeOffset editedAt) ? editedAt : null;
    }

    /// <summary>Changes made until the returned scope is disposed are not stamped (a received change being applied).</summary>
    public IDisposable SuppressStamping()
    {
        _suppressed++;
        return new SuppressionScope(this);
    }

    private void OnProfilesChanged(ProfileDocument current)
    {
        // Compared as JSON: a change rebuilds the lists, so record equality would see every change as an edit.
        string currentJson = WithoutDayPlan(current);
        bool edited = currentJson != _profilesJson;
        _profilesJson = currentJson;
        if (edited)
        {
            Stamp(SyncDocument.ProfilesKey);
        }
    }

    private static string WithoutDayPlan(ProfileDocument document)
    {
        return ProfileDocumentSerializer.Serialize(document with { DayPlan = DayPlan.Default });
    }

    private void Stamp(string key)
    {
        if (_suppressed > 0)
        {
            return;
        }

        _editedAt = new Dictionary<string, DateTimeOffset>(_editedAt) { [key] = _time.GetUtcNow() };
        Dictionary<string, DateTimeOffset> toSave = _editedAt;
        _saving = _saving.ContinueWith(
            async _ =>
            {
                try
                {
                    await _store.SaveAsync(DocumentKey, toSave).ConfigureAwait(false);
                }
                catch (SqliteException)
                {
                    // Kept in memory; the next stamp saves again.
                }
            },
            TaskScheduler.Default).Unwrap();
    }

    private sealed class SuppressionScope(SyncEditTimesService owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner._suppressed--;
            }
        }
    }
}
