using System.Text.Json.Nodes;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Settings kept per mode (ADR 69). A per-mode service reads and writes the active mode's copy of its document
/// (<see cref="Key"/>); ProductaBuddy's copy starts as AccountabiliBuddy's the first time it is read (<see cref="ReadKeyBlocking"/>).
/// When the mode switches, or the compare view changes a value, <see cref="Reload"/> asks every per-mode service to read its
/// document again. Device-local, never synced. Use from the UI thread only.
/// </summary>
public sealed class ModeSettings
{
    /// <summary>ProductaBuddy's copy of a document: the AccountabiliBuddy key plus this.</summary>
    public const string CompanionSuffix = ".pb";

    private readonly SqliteJsonDocumentStore _store;
    private GuidanceMode _mode;

    public ModeSettings(SqliteJsonDocumentStore store, GuidanceModeService guidance)
    {
        _store = store;
        _mode = guidance.State.Mode;
        guidance.Changed += (_, _) =>
        {
            if (guidance.State.Mode != _mode)
            {
                _mode = guidance.State.Mode;
                Reload?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>The mode whose settings are in effect.</summary>
    public GuidanceMode Mode => _mode;

    /// <summary>The mode switched, or the compare view changed a value: per-mode services read their document again.</summary>
    public event EventHandler? Reload;

    public static string KeyFor(string baseKey, GuidanceMode mode) => mode == GuidanceMode.Companion ? baseKey + CompanionSuffix : baseKey;

    /// <summary>The active mode's copy of <paramref name="baseKey"/>, to read and write.</summary>
    public string Key(string baseKey) => KeyFor(baseKey, _mode);

    /// <summary>
    /// The key to read now. In ProductaBuddy with no copy yet, one is made first: AccountabiliBuddy's document (or
    /// <paramref name="fallback"/> when it has none), passed through <paramref name="companionStart"/> (e.g. the start-of-day
    /// assistant off).
    /// </summary>
    public string ReadKeyBlocking<T>(string baseKey, T fallback, Func<T, T>? companionStart = null) where T : class
    {
        string key = Key(baseKey);
        if (_mode != GuidanceMode.Companion || Task.Run(() => _store.LoadAsync<JsonNode>(key)).GetAwaiter().GetResult() is not null)
        {
            return key;
        }

        T guarding = Task.Run(() => _store.LoadAsync<T>(baseKey)).GetAwaiter().GetResult() ?? fallback;
        T start = companionStart is null ? guarding : companionStart(guarding);
        Task.Run(() => _store.SaveAsync(key, start)).GetAwaiter().GetResult();
        return key;
    }

    /// <summary>The per-mode settings whose two values differ, in the registry's order. A copy not made yet reads as AccountabiliBuddy's.</summary>
    public async Task<IReadOnlyList<ModeSettingRow>> CompareAsync()
    {
        var rows = new List<ModeSettingRow>();
        foreach (string key in PerModeSettings.DocumentKeys)
        {
            (JsonNode? guarding, JsonNode? companion) = await LoadBothAsync(key);
            List<PerModeSetting> settings = PerModeSettings.All.Where(setting => setting.DocumentKey == key).ToList();
            IReadOnlyList<string> differing = ModeSettingsDiff.DifferingPaths(guarding, companion, settings.Select(setting => setting.Path));
            rows.AddRange(settings
                .Where(setting => differing.Contains(setting.Path))
                .Select(setting => new ModeSettingRow(setting, ModeSettingsDiff.At(guarding, setting.Path), ModeSettingsDiff.At(companion, setting.Path))));
        }

        return rows;
    }

    /// <summary>
    /// Makes both modes agree on each picked setting: AccountabiliBuddy's value when <c>UseGuarding</c>, else ProductaBuddy's.
    /// The active mode's services then read their documents again.
    /// </summary>
    public async Task ApplyAsync(IReadOnlyList<(ModeSettingRow Row, bool UseGuarding)> picks)
    {
        foreach (IGrouping<string, (ModeSettingRow Row, bool UseGuarding)> document in picks.GroupBy(pick => pick.Row.Setting.DocumentKey))
        {
            (JsonNode? guarding, JsonNode? companion) = await LoadBothAsync(document.Key);
            foreach ((ModeSettingRow row, bool useGuarding) in document)
            {
                JsonNode? value = useGuarding ? row.Guarding : row.Companion;
                guarding = ModeSettingsDiff.WithValue(guarding, row.Setting.Path, value);
                companion = ModeSettingsDiff.WithValue(companion, row.Setting.Path, value);
            }

            await _store.SaveAsync(document.Key, guarding);
            await _store.SaveAsync(document.Key + CompanionSuffix, companion);
        }

        Reload?.Invoke(this, EventArgs.Empty);
    }

    // Both copies with today's names; ProductaBuddy's not made yet reads as it would start (ReadKeyBlocking does the same).
    private async Task<(JsonNode? Guarding, JsonNode? Companion)> LoadBothAsync(string key)
    {
        JsonNode? guarding = ModeSettingsDiff.CamelCased(await _store.LoadAsync<JsonNode>(key));
        JsonNode? companion = ModeSettingsDiff.CamelCased(await _store.LoadAsync<JsonNode>(key + CompanionSuffix))
            ?? PerModeSettings.CompanionStart(key, guarding);
        return (guarding, companion);
    }
}
