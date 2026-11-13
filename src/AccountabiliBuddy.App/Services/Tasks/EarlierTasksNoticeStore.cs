using System.Text.Json;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// The one-time card "N tasks from the earlier version are in To sort" (MIG1): the count the migration moved and whether the user
/// closed the card, kept in the local document "tasks.earlierNotice" (this PC only, never synced; no text, only a number). Written by
/// the migration after it switched over, read once at start; the card shows while a count is waiting, and × closes it for good.
/// </summary>
public sealed class EarlierTasksNoticeStore
{
    public const string DocumentKey = "tasks.earlierNotice";

    private readonly SqliteJsonDocumentStore _documents;

    public EarlierTasksNoticeStore(SqliteJsonDocumentStore documents)
    {
        _documents = documents;
    }

    /// <summary>How many open tasks the migration moved into To sort; 0 when there is nothing to tell (or the card was closed).</summary>
    public int Count { get; private set; }

    /// <summary>The count changed (on the thread that changed it).</summary>
    public event EventHandler? Changed;

    /// <summary>Reads the stored notice; an unreadable one shows no card (a notice must never stop the app starting).</summary>
    public void LoadBlocking()
    {
        try
        {
            Notice? stored = Task.Run(() => _documents.LoadAsync<Notice>(DocumentKey)).GetAwaiter().GetResult();
            Count = stored is { Dismissed: false, Count: > 0 } ? stored.Count : 0;
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or InvalidOperationException or NotSupportedException)
        {
            Count = 0;
        }
    }

    /// <summary>The migration moved <paramref name="count"/> open tasks into To sort: the card shows (nothing for 0). A failure to save is not worth stopping for.</summary>
    public void RecordBlocking(int count)
    {
        if (count <= 0)
        {
            return;
        }

        try
        {
            Task.Run(() => _documents.SaveAsync(DocumentKey, new Notice(count, Dismissed: false))).GetAwaiter().GetResult();
            Count = count;
        }
        catch (SqliteException)
        {
            Count = 0;
        }
    }

    /// <summary>× on the card: closed for good (the count stays stored, marked closed).</summary>
    public async Task DismissAsync()
    {
        int count = Count;
        if (count == 0)
        {
            return;
        }

        Count = 0;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await _documents.SaveAsync(DocumentKey, new Notice(count, Dismissed: true));
        }
        catch (SqliteException)
        {
            // Closed for this run; it may show once more at the next start.
        }
    }

    private sealed record Notice(int Count, bool Dismissed);
}
