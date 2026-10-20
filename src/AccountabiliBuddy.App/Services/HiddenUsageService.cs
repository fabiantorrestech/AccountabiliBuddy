using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Apps and sites the user hid from History's "Most used" list (documents "history.hidden"), e.g. background
/// helpers like a keyboard driver. Keyed by the real process name or domain, ignoring case. Display only: their
/// time is still recorded and still counts in focus totals. Use from the UI thread only.
/// </summary>
public sealed class HiddenUsageService
{
    private const string DocumentKey = "history.hidden";

    private readonly SqliteJsonDocumentStore _store;
    private HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);

    public HiddenUsageService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public event EventHandler? Changed;

    public IReadOnlyCollection<string> HiddenNames => _hidden;

    public void LoadBlocking()
    {
        try
        {
            List<string>? saved = Task.Run(() => _store.LoadAsync<List<string>>(DocumentKey)).GetAwaiter().GetResult();
            _hidden = new HashSet<string>(saved ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool IsHidden(string realName)
    {
        return _hidden.Contains(realName);
    }

    public async Task SetHiddenAsync(IEnumerable<string> realNames, bool hidden)
    {
        foreach (string name in realNames)
        {
            if (hidden)
            {
                _hidden.Add(name);
            }
            else
            {
                _hidden.Remove(name);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        await _store.SaveAsync(DocumentKey, _hidden.Order(StringComparer.OrdinalIgnoreCase).ToList());
    }
}
