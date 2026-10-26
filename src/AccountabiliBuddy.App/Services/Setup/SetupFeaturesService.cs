using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Setup;

/// <summary>
/// The migration assistant's state ("setup.features"): features skipped on purpose, and the version whose Today card
/// was dismissed. <see cref="Pending"/> = features that apply here, aren't set up and weren't skipped;
/// <see cref="NewSinceUpgrade"/> = those that arrived after the version this data ran with before (from "app.version").
/// </summary>
public sealed class SetupFeaturesService
{
    public const string DocumentKey = "setup.features";

    private readonly SqliteJsonDocumentStore _store;
    private readonly Func<IReadOnlyList<SetupFeature>> _features;
    private readonly string _appVersion;
    private FeaturesState _state = new(new Dictionary<string, DateTimeOffset>(), null);
    private string? _previousVersion;

    public SetupFeaturesService(SqliteJsonDocumentStore store, Func<IReadOnlyList<SetupFeature>> features, string appVersion)
    {
        _store = store;
        _features = features;
        _appVersion = appVersion;
    }

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            _state = Task.Run(() => _store.LoadAsync<FeaturesState>(DocumentKey)).GetAwaiter().GetResult() ?? _state;
            _previousVersion = Task.Run(() => _store.LoadAsync<AppVersionRecord>("app.version")).GetAwaiter().GetResult()?.Previous;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // Nothing skipped; no upgrade known.
        }
    }

    public IReadOnlyList<SetupFeature> Pending =>
        _features().Where(feature => !_state.Skipped.ContainsKey(feature.Id) && feature.IsRelevant() && !feature.IsSetUp()).ToList();

    /// <summary>Pending features that arrived after the version this data ran with before (none on a fresh install).</summary>
    public IReadOnlyList<SetupFeature> NewSinceUpgrade =>
        _previousVersion is null ? Array.Empty<SetupFeature>() : Pending.Where(feature => Newer(feature.Since, _previousVersion)).ToList();

    /// <summary>The quiet Today card: shown after an upgrade with something new to set up, until dismissed for this version.</summary>
    public bool ShowsCard => _state.CardDismissedFor != _appVersion && NewSinceUpgrade.Count > 0;

    public async Task DismissCardAsync()
    {
        _state = _state with { CardDismissedFor = _appVersion };
        await SaveAsync();
    }

    public async Task SkipAsync(string id)
    {
        var skipped = new Dictionary<string, DateTimeOffset>(_state.Skipped) { [id] = DateTimeOffset.UtcNow };
        _state = _state with { Skipped = skipped };
        await SaveAsync();
    }

    /// <summary>Something a feature checks changed (e.g. it was just set up): the card may go.</summary>
    public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);

    private static bool Newer(string version, string than) =>
        Version.TryParse(version, out Version? a) && Version.TryParse(than, out Version? b) && a > b;

    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveAsync(DocumentKey, _state);
        }
        catch (SqliteException)
        {
            // Kept in memory.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record FeaturesState(Dictionary<string, DateTimeOffset> Skipped, string? CardDismissedFor);
}
