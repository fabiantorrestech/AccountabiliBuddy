using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What "Leave it" remembers on the Plan helper: the suggestions the user dismissed, each by its key (kind and day) and the
/// date it was dismissed, kept 14 days (document "planHelperDismissed", this PC only, never synced). Dismissing hides a
/// suggestion; it never changes the plan or relaxes anything. Use from the UI thread only.
/// </summary>
public sealed class PlanHelperDismissals
{
    private const string DocumentKey = "planHelperDismissed";

    /// <summary>How many days a dismissal is kept.</summary>
    public const int KeptDays = 14;

    private readonly SqliteJsonDocumentStore _store;
    private readonly Func<DateOnly> _today;
    private readonly Dictionary<string, DateOnly> _dismissed = new();
    private bool _used;

    /// <param name="today">Today's date from the trusted clock.</param>
    public PlanHelperDismissals(SqliteJsonDocumentStore store, Func<DateOnly> today)
    {
        _store = store;
        _today = today;
    }

    /// <summary>Raised after a dismissal is added.</summary>
    public event EventHandler? Changed;

    /// <summary>True once the user has acted on a suggestion or left one (the migration assistant's "set up?" check).</summary>
    public bool HasUsed => _used;

    public void LoadBlocking()
    {
        try
        {
            DismissedState? state = Task.Run(() => _store.LoadAsync<DismissedState>(DocumentKey)).GetAwaiter().GetResult();
            _dismissed.Clear();
            _used = state?.Used == true;
            foreach (DismissedEntry entry in state?.Entries ?? [])
            {
                _dismissed[entry.Key] = entry.On;
            }

            Prune();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _dismissed.Clear();
        }
    }

    public bool IsDismissed(string key) => _dismissed.ContainsKey(key);

    public async Task DismissAsync(string key)
    {
        _dismissed[key] = _today();
        _used = true;
        Prune();
        await SaveAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Remembers that a suggestion was acted on (Move it, Start, Change a day).</summary>
    public async Task MarkUsedAsync()
    {
        if (_used)
        {
            return;
        }

        _used = true;
        await SaveAsync();
    }

    private Task SaveAsync()
    {
        return _store.SaveAsync(DocumentKey, new DismissedState(_dismissed.Select(pair => new DismissedEntry(pair.Key, pair.Value)).ToList(), _used));
    }

    // Dismissals older than the kept days are forgotten.
    private void Prune()
    {
        DateOnly oldest = _today().AddDays(-KeptDays);
        foreach (string key in _dismissed.Where(pair => pair.Value < oldest).Select(pair => pair.Key).ToList())
        {
            _dismissed.Remove(key);
        }
    }

    private sealed record DismissedState(List<DismissedEntry> Entries, bool Used = false);

    private sealed record DismissedEntry(string Key, DateOnly On);
}
