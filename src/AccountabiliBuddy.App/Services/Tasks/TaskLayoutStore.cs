using System.Text.Json;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// Keeps <see cref="TaskLayoutPreferences"/> (densities, folds, column order, the Shelf's split and folded columns) in the local document "tasks.layout" (this PC only, never synced; a device setting in
/// backups). Read once at start; every change is saved and announced at once (it only changes how the pages look).
/// </summary>
public sealed class TaskLayoutStore
{
    public const string DocumentKey = "tasks.layout";

    private readonly SqliteJsonDocumentStore _documents;

    public TaskLayoutStore(SqliteJsonDocumentStore documents)
    {
        _documents = documents;
    }

    public TaskLayoutPreferences Current { get; private set; } = TaskLayoutPreferences.Default;

    /// <summary>A preference changed (on the UI thread, before it is saved).</summary>
    public event EventHandler? Changed;

    /// <summary>Reads the saved layout; an unreadable one leaves the defaults (a bad preference must never stop the app starting).</summary>
    public void LoadBlocking()
    {
        try
        {
            Current = Task.Run(() => _documents.LoadAsync<TaskLayoutPreferences>(DocumentKey)).GetAwaiter().GetResult() ?? TaskLayoutPreferences.Default;
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or InvalidOperationException or NotSupportedException)
        {
            Current = TaskLayoutPreferences.Default;
        }
    }

    public async Task UpdateAsync(Func<TaskLayoutPreferences, TaskLayoutPreferences> change)
    {
        TaskLayoutPreferences next = change(Current);
        if (next == Current)
        {
            return;
        }

        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await _documents.SaveAsync(DocumentKey, next);
        }
        catch (SqliteException)
        {
            // Not remembered this time; the page keeps it until the app closes.
        }
    }

    public Task SetCompactAsync(bool compact) => UpdateAsync(current => current with { Compact = compact });

    /// <summary>"Fade controls you're not using" (Settings › Tasks, Tasks › Options): at once, the same in both modes.</summary>
    public Task SetFadeIdleControlsAsync(bool on) => UpdateAsync(current => current with { FadeIdleControls = on });

    public Task SetSplitAsync(int percent) => UpdateAsync(current => current with
    {
        TodaySplit = Math.Clamp(percent, TaskLayoutPreferences.MinSplit, TaskLayoutPreferences.MaxSplit),
    });

    public Task SetExpandAllAsync(string panel, bool expanded) => UpdateAsync(current =>
    {
        var panels = new Dictionary<string, bool>(current.ExpandAll ?? new Dictionary<string, bool>()) { [panel] = expanded };
        return current with { ExpandAll = panels };
    });

    /// <summary>Round 2 R3: the Shelf's columns of one grouping ("bins", "label", "tag") in this order from now on (this PC only).</summary>
    public Task SetColumnOrderAsync(string grouping, IReadOnlyList<string> order) => UpdateAsync(current =>
    {
        if (current.ColumnOrderOf(grouping) is { } stored && stored.SequenceEqual(order, StringComparer.Ordinal))
        {
            return current;
        }

        var orders = new Dictionary<string, IReadOnlyList<string>>(current.ColumnOrder ?? new Dictionary<string, IReadOnlyList<string>>())
        {
            [grouping] = order.ToList(),
        };
        return current with { ColumnOrder = orders };
    });

    /// <summary>Tasks redesign 4c: Next's share of the Next and To sort width, in percent (clamped 30–75; this PC only).</summary>
    public Task SetShelfSplitAsync(int percent) => UpdateAsync(current => current with
    {
        ShelfSplit = Math.Clamp(percent, TaskLayoutPreferences.MinShelfSplit, TaskLayoutPreferences.MaxShelfSplit),
    });

    /// <summary>Tasks redesign 4c: the Shelf columns <paramref name="keys"/> folded to a rail (true) or open (false); this PC only.</summary>
    public Task SetColumnsFoldedAsync(IReadOnlyCollection<string> keys, bool folded) => UpdateAsync(current =>
    {
        List<string> list = (current.FoldedColumns ?? []).ToList();
        bool changed = false;
        foreach (string key in keys)
        {
            bool has = list.Contains(key, StringComparer.Ordinal);
            if (folded && !has)
            {
                list.Add(key);
                changed = true;
            }
            else if (!folded && has)
            {
                list.RemoveAll(item => string.Equals(item, key, StringComparison.Ordinal));
                changed = true;
            }
        }

        return changed ? current with { FoldedColumns = list } : current;
    });

    public Task TogglePinAsync(string section) => UpdateAsync(current =>
    {
        List<string> pins = current.PinnedNow.ToList();
        if (!pins.Remove(section))
        {
            pins.Add(section);
        }

        return current with { Pinned = pins };
    });
}
