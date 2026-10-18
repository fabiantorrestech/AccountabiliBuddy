using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Remembers screen-reading (OCR) results per window view and decides when a view is due to be read again,
/// so OCR stays the rare, last step of the detection cascade. A "view" is a window plus its title: switching
/// tabs or documents changes the title and makes the new view due at once; the same view is re-read at most
/// once per <see cref="Freshness"/>. Failed reads count as reads, so a window that cannot be captured is not
/// retried in a tight loop.
/// Not thread-safe: call from a single thread.
/// </summary>
public sealed class ScreenTextCache
{
    public static TimeSpan Freshness { get; } = TimeSpan.FromSeconds(60);

    private const int MaxEntries = 64;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

    /// <returns>The last text read for this view, even if it is due for a refresh; null if never read.</returns>
    public string? GetText(ActivitySnapshot snapshot)
    {
        return _entries.TryGetValue(KeyOf(snapshot), out Entry? entry) ? entry.Text : null;
    }

    /// <summary>True when this view has never been read, or its last read is older than <see cref="Freshness"/>.</summary>
    public bool IsDue(ActivitySnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.IsEmptyDesktop)
        {
            return false;
        }

        string key = KeyOf(snapshot);
        return !_pending.Contains(key)
            && (!_entries.TryGetValue(key, out Entry? entry) || now - entry.ReadAt >= Freshness);
    }

    public void MarkPending(ActivitySnapshot snapshot)
    {
        _pending.Add(KeyOf(snapshot));
    }

    /// <param name="text">The recognized text, or null if the window could not be read.</param>
    public void Store(ActivitySnapshot snapshot, string? text, DateTimeOffset readAt)
    {
        string key = KeyOf(snapshot);
        _pending.Remove(key);

        if (_entries.Count >= MaxEntries && !_entries.ContainsKey(key))
        {
            // Forget the oldest read; the cache only needs recent views.
            string oldest = _entries.MinBy(pair => pair.Value.ReadAt).Key;
            _entries.Remove(oldest);
        }

        _entries[key] = new Entry(string.IsNullOrWhiteSpace(text) ? null : text, readAt);
    }

    public void Clear()
    {
        _entries.Clear();
        _pending.Clear();
    }

    private static string KeyOf(ActivitySnapshot snapshot)
    {
        return $"{snapshot.WindowId}|{snapshot.WindowTitle}";
    }

    private sealed record Entry(string? Text, DateTimeOffset ReadAt);
}
