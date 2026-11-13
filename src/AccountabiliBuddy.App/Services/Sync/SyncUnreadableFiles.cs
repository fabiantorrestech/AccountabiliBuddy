using System.Security.Cryptography;
using System.Text;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The files the sync methods hold that this PC does not take in (SYNC1b), by method and file: damaged, too big, from a newer app, an
/// old group's leftover, this PC's own stale copy. In memory only (every start reads everything again). The three sync services report
/// what a pass found and clear a file once it reads or is gone; the troubleshooting section lists it. "Ignore" only hides a line; it
/// never feeds <see cref="ReceivedDevicesService.LastProblem"/> and never changes what a service does. Use from the UI thread.
/// </summary>
public sealed class SyncUnreadableFiles
{
    private readonly Dictionary<(string Method, string Name), SyncUnreadableFile> _files = [];
    private readonly TimeProvider _time;

    public SyncUnreadableFiles(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>Raised when the list changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Every entry, newest first.</summary>
    public IReadOnlyList<SyncUnreadableFile> All => _files.Values.OrderByDescending(file => file.At).ThenBy(file => file.Key, StringComparer.Ordinal).ToList();

    /// <summary>The entries shown: not ignored.</summary>
    public IReadOnlyList<SyncUnreadableFile> Shown => All.Where(file => !file.Ignored).ToList();

    /// <summary>A file did not read (or is a leftover): recorded, keeping an earlier "Ignore" of the same file with the same reason.</summary>
    public void Report(string method, string name, string reason, Guid? device, bool ownStale)
    {
        (string, string) key = (method, name);
        bool keepIgnored = _files.TryGetValue(key, out SyncUnreadableFile? existing) && existing.Ignored && existing.Reason == reason;
        var entry = new SyncUnreadableFile(method, KeyOf(method, name), name, reason, existing is not null && existing.Reason == reason ? existing.At : _time.GetUtcNow(), device, ownStale, keepIgnored);
        if (existing == entry)
        {
            return;
        }

        _files[key] = entry;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The file reads now (or its name is no longer a problem): it leaves the list.</summary>
    public void Clear(string method, string name)
    {
        if (_files.Remove((method, name)))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Drops the method's entries whose file is no longer there (a full listing of the method).</summary>
    public void Prune(string method, IReadOnlySet<string> existingNames)
    {
        bool changed = false;
        foreach ((string Method, string Name) key in _files.Keys.Where(key => key.Method == method && !existingNames.Contains(key.Name)).ToList())
        {
            changed |= _files.Remove(key);
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Drops every entry (the group changed or the method was switched off).</summary>
    public void ClearAll(string? method = null)
    {
        int before = _files.Count;
        foreach ((string Method, string Name) key in _files.Keys.Where(key => method is null || key.Method == method).ToList())
        {
            _files.Remove(key);
        }

        if (_files.Count != before)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Hides one line. The file, its method and every other setting stay as they are.</summary>
    public void Ignore(string method, string key)
    {
        KeyValuePair<(string Method, string Name), SyncUnreadableFile> pair = _files.FirstOrDefault(item => item.Key.Method == method && item.Value.Key == key);
        if (pair.Value is null || pair.Value.Ignored)
        {
            return;
        }

        _files[pair.Key] = pair.Value with { Ignored = true };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The entry with this key on this method; null when it is gone.</summary>
    public SyncUnreadableFile? Find(string method, string key) => _files.Values.FirstOrDefault(file => file.Method == method && file.Key == key);

    private static string KeyOf(string method, string name) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(method + "|" + name)).AsSpan(0, 4));
}
