using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Names the user gave apps and sites for History (documents "history.names"), e.g. "us.nothing.tech" →
/// "Nothing Store". Keyed by the real name, ignoring case. Display only: rules keep matching the real process
/// name or domain. Use from the UI thread only.
/// </summary>
public sealed class DisplayNamesService
{
    private const string DocumentKey = "history.names";

    private readonly SqliteJsonDocumentStore _store;
    private Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    public DisplayNamesService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            Dictionary<string, string>? saved = Task.Run(() => _store.LoadAsync<Dictionary<string, string>>(DocumentKey)).GetAwaiter().GetResult();
            _names = saved is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(saved, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>The name to show for an app or site: the user's name for it, or the real one.</summary>
    public string NameFor(string realName)
    {
        return _names.TryGetValue(realName, out string? name) ? name : realName;
    }

    /// <summary>Gives these real names one display name; an empty name goes back to the real ones.</summary>
    public async Task RenameAsync(IEnumerable<string> realNames, string? displayName)
    {
        string? name = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        foreach (string realName in realNames)
        {
            if (name is null || string.Equals(name, realName, StringComparison.Ordinal))
            {
                _names.Remove(realName);
            }
            else
            {
                _names[realName] = name;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        await _store.SaveAsync(DocumentKey, _names);
    }
}
