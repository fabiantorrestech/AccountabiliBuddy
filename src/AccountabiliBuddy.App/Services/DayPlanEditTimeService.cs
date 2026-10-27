using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// When the day plan last changed, from any source (the Day plan tab, Exceptions, a paired phone), for the phone's
/// conflict prompt (ADR 19). Kept with the plan's version in the documents table ("dayplan.editedAt"): a version that
/// differs from the saved one means the plan changed. A phone's edit records the phone's own edit time. UI thread only.
/// </summary>
public sealed class DayPlanEditTimeService
{
    private const string DocumentKey = "dayplan.editedAt";

    private readonly ConfigurationService _configuration;
    private readonly SqliteJsonDocumentStore _store;
    private readonly Func<DateTimeOffset> _now;
    private string? _version;
    private DateTimeOffset? _expectedEditTime;

    public DayPlanEditTimeService(ConfigurationService configuration, SqliteJsonDocumentStore store, Func<DateTimeOffset> now)
    {
        _configuration = configuration;
        _store = store;
        _now = now;
        _configuration.Changed += (_, _) => _ = OnConfigurationChangedAsync();
    }

    /// <summary>Null until the plan is first seen to change.</summary>
    public DateTimeOffset? Current { get; private set; }

    public void LoadBlocking()
    {
        try
        {
            DayPlanEditTime? saved = Task.Run(() => _store.LoadAsync<DayPlanEditTime>(DocumentKey)).GetAwaiter().GetResult();
            string version = DayPlanVersion.Of(_configuration.Current.DayPlan);
            _version = version;
            // The first start after the v12 or v13 upgrade (ADR 47): the saved version is the earlier schema's one of this same, migrated plan.
            bool upgraded = saved is not null && saved.Version != version && DayPlanVersion.IsEarlierSchemaVersionOf(saved.Version, _configuration.Current.DayPlan);
            // Changed while the app wasn't watching (e.g. a restore): when isn't known.
            Current = saved is not null && (saved.Version == version || upgraded) ? saved.EditedAt : null;
            if (upgraded)
            {
                Task.Run(() => _store.SaveAsync(DocumentKey, new DayPlanEditTime(version, Current))).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Current = null;
        }
    }

    /// <summary>The next plan change comes from a device edit made at this time (not later than now).</summary>
    public void ExpectEdit(DateTimeOffset editedAt)
    {
        DateTimeOffset now = _now();
        _expectedEditTime = editedAt > now ? now : editedAt;
    }

    private async Task OnConfigurationChangedAsync()
    {
        string version = DayPlanVersion.Of(_configuration.Current.DayPlan);
        if (version == _version)
        {
            return;
        }

        _version = version;
        Current = _expectedEditTime ?? _now();
        _expectedEditTime = null;
        try
        {
            await _store.SaveAsync(DocumentKey, new DayPlanEditTime(version, Current));
        }
        catch (SqliteException)
        {
            // Kept in memory; the next change saves again.
        }
    }

    private sealed record DayPlanEditTime(string Version, DateTimeOffset? EditedAt);
}
