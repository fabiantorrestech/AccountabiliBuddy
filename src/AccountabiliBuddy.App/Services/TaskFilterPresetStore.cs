using System.Text.Json;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Loads and saves the Tasks page's saved filters in the local document "tasks.filterPresets": DPAPI-encrypted, since a preset holds
/// search words and names the user typed. This PC only: never synced and never in a backup, like the filter's "tasks.view".
/// </summary>
public sealed class TaskFilterPresetStore
{
    private const string DocumentKey = "tasks.filterPresets";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _documents;
    private readonly IDataProtector _protector;

    public TaskFilterPresetStore(SqliteJsonDocumentStore documents, IDataProtector protector)
    {
        _documents = documents;
        _protector = protector;
    }

    /// <summary>The saved filters; none when there are none or they can't be read (a bad preference must never stop the app starting).</summary>
    public IReadOnlyList<TaskFilterPreset> Load()
    {
        try
        {
            Stored? stored = Task.Run(() => _documents.LoadAsync<Stored>(DocumentKey)).GetAwaiter().GetResult();
            if (string.IsNullOrEmpty(stored?.Data) || _protector.Unprotect(Convert.FromBase64String(stored.Data)) is not string json)
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<TaskFilterPreset>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException or InvalidOperationException or NotSupportedException)
        {
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<TaskFilterPreset> presets)
    {
        try
        {
            string data = Convert.ToBase64String(_protector.Protect(JsonSerializer.Serialize(presets, JsonOptions)));
            await _documents.SaveAsync(DocumentKey, new Stored(data));
        }
        catch (SqliteException)
        {
            // Not remembered this time; nothing else depends on it.
        }
    }

    private sealed record Stored(string Data);
}
