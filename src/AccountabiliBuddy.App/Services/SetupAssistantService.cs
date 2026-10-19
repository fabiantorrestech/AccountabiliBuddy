using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Remembers (documents key "setup.assistant") whether the setup assistant has been shown, so it opens by itself
/// only on the first launch. It can always be opened again from the Setup tab.
/// </summary>
public sealed class SetupAssistantService
{
    private const string DocumentKey = "setup.assistant";

    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;

    public SetupAssistantService(SqliteJsonDocumentStore store, TimeProvider time)
    {
        _store = store;
        _time = time;
    }

    public bool HasBeenShown { get; private set; }

    public void LoadBlocking()
    {
        try
        {
            HasBeenShown = Task.Run(() => _store.LoadAsync<SetupAssistantRecord>(DocumentKey)).GetAwaiter().GetResult() is not null;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // Unreadable: treat as shown rather than opening the assistant on every launch.
            HasBeenShown = true;
        }
    }

    public async Task MarkShownAsync()
    {
        HasBeenShown = true;
        try
        {
            await _store.SaveAsync(DocumentKey, new SetupAssistantRecord(_time.GetUtcNow()));
        }
        catch (SqliteException)
        {
            // Best effort: at worst the assistant opens once more at the next launch.
        }
    }
}
