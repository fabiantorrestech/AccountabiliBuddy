using System.Security.Cryptography;
using System.Text;

namespace AccountabiliBuddy.Relay;

/// <summary>
/// The relay's disk storage: <c>&lt;data&gt;/&lt;gid&gt;/&lt;name&gt;</c> plus an <c>owner.sha256</c> file per group holding the
/// hash of the first bearer token seen. The bytes are opaque; nothing here parses or decrypts them. Group ids and file
/// names are validated against <see cref="RelayNames"/> by every public method, so a path part never reaches the disk
/// unchecked. Writes go to a temporary file first and are renamed into place, so a reader never sees half a file. All
/// disk work runs under one lock: traffic is a handful of small files per group, so simple beats fast here.
/// </summary>
internal sealed class GroupStore
{
    private const string OwnerFileName = "owner.sha256";
    private const string TempMarker = ".tmp-";

    private readonly string _root;
    private readonly object _gate = new();
    private readonly Dictionary<string, CachedETag> _etags = new(StringComparer.Ordinal);

    private readonly long _maxDiskBytes;
    private long _usedBytes;

    /// <param name="root">The data folder.</param>
    /// <param name="maxDiskBytes">Writes that would grow the stored files past this are refused (disk full, 507).</param>
    public GroupStore(string root, long maxDiskBytes)
    {
        _root = Path.GetFullPath(root);
        _maxDiskBytes = maxDiskBytes;
        Directory.CreateDirectory(_root);
        _usedBytes = MeasureStoredBytes();
    }

    /// <summary>The SHA-256 of the bytes as base64url without padding, the format of the pinned certificate fingerprint.</summary>
    public static string ComputeETag(ReadOnlySpan<byte> bytes)
    {
        return Base64Url(SHA256.HashData(bytes));
    }

    /// <summary>The hash of a bearer token as lower-case hex, what the owner file holds.</summary>
    public static string HashToken(string token)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    public GroupAccess GetAccess(string gid, string tokenHash)
    {
        lock (_gate)
        {
            string? owner = ReadOwner(GroupDirectory(gid));
            if (owner is null)
            {
                return GroupAccess.NoGroup;
            }

            return OwnerMatches(owner, tokenHash) ? GroupAccess.Owner : GroupAccess.NotOwner;
        }
    }

    public PutResult Put(string gid, string tokenHash, string name, byte[] bytes)
    {
        lock (_gate)
        {
            string directory = GroupDirectory(gid);
            string? owner = ReadOwner(directory);
            if (owner is not null && !OwnerMatches(owner, tokenHash))
            {
                return new PutResult(PutStatus.NotOwner, string.Empty, default);
            }

            string path = FilePath(directory, name);
            string etag = ComputeETag(bytes);
            var existing = new FileInfo(path);
            long oldLength = existing.Exists ? existing.Length : 0;
            long growth = bytes.Length - oldLength;
            bool sameBytes = existing.Exists && existing.Length == bytes.Length && ETagOf(existing) == etag;
            if (!sameBytes && growth > 0 && _usedBytes + growth > _maxDiskBytes)
            {
                return new PutResult(PutStatus.DiskFull, string.Empty, default);
            }

            if (owner is null)
            {
                if (CountGroups() >= RelayLimits.MaxGroups)
                {
                    return new PutResult(PutStatus.TooManyGroups, string.Empty, default);
                }

                Directory.CreateDirectory(directory);
                WriteAtomically(Path.Combine(directory, OwnerFileName), Encoding.ASCII.GetBytes(tokenHash));
            }

            if (sameBytes)
            {
                // The same bytes again (a retry, or an echo of a push): keep the file alive but don't announce a change.
                existing.LastWriteTimeUtc = DateTime.UtcNow;
                existing.Refresh();
                _etags[path] = new CachedETag(existing.Length, existing.LastWriteTimeUtc.Ticks, etag);
                return new PutResult(PutStatus.Unchanged, etag, new DateTimeOffset(existing.LastWriteTimeUtc, TimeSpan.Zero));
            }

            if (!existing.Exists && CountFiles(directory) >= RelayLimits.MaxFilesPerGroup)
            {
                return new PutResult(PutStatus.TooManyFiles, string.Empty, default);
            }

            WriteAtomically(path, bytes);
            _usedBytes += growth;
            var written = new FileInfo(path);
            _etags[path] = new CachedETag(written.Length, written.LastWriteTimeUtc.Ticks, etag);
            return new PutResult(PutStatus.Stored, etag, new DateTimeOffset(written.LastWriteTimeUtc, TimeSpan.Zero));
        }
    }

    public IReadOnlyList<StoredFile> List(string gid)
    {
        lock (_gate)
        {
            string directory = GroupDirectory(gid);
            var files = new List<StoredFile>();
            if (!Directory.Exists(directory))
            {
                return files;
            }

            foreach (string path in Directory.EnumerateFiles(directory))
            {
                string name = Path.GetFileName(path);
                if (!RelayNames.IsFileName(name))
                {
                    continue;
                }

                var info = new FileInfo(path);
                files.Add(new StoredFile(name, ETagOf(info), info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
            }

            files.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return files;
        }
    }

    /// <summary>The stored bytes and their etag, or null when there is no such file.</summary>
    public (byte[] Bytes, string ETag)? TryRead(string gid, string name)
    {
        lock (_gate)
        {
            string path = FilePath(GroupDirectory(gid), name);
            if (!File.Exists(path))
            {
                return null;
            }

            byte[] bytes = File.ReadAllBytes(path);
            return (bytes, ComputeETag(bytes));
        }
    }

    /// <summary>Removes one file. The group (and its owner) stays, so the same code can still write and rejoin.</summary>
    public bool Delete(string gid, string name)
    {
        lock (_gate)
        {
            string path = FilePath(GroupDirectory(gid), name);
            if (!File.Exists(path))
            {
                return false;
            }

            _usedBytes -= new FileInfo(path).Length;
            File.Delete(path);
            _etags.Remove(path);
            return true;
        }
    }

    /// <summary>
    /// Deletes files untouched for <see cref="RelayLimits.MaxFileAge"/>, leftover temporary files, and groups that
    /// have no files left and whose owner record is just as old. Returns how many files were removed.
    /// </summary>
    public int RemoveExpired(DateTimeOffset now)
    {
        lock (_gate)
        {
            int removed = 0;
            DateTime cutoff = (now - RelayLimits.MaxFileAge).UtcDateTime;
            foreach (string directory in Directory.EnumerateDirectories(_root))
            {
                if (!RelayNames.IsGroupId(Path.GetFileName(directory)))
                {
                    continue;
                }

                removed += RemoveExpiredFiles(directory, cutoff, now.UtcDateTime.AddHours(-1));
                RemoveEmptyGroup(directory, cutoff);
            }

            return removed;
        }
    }

    private int RemoveExpiredFiles(string directory, DateTime cutoff, DateTime tempCutoff)
    {
        int removed = 0;
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            string name = Path.GetFileName(path);
            DateTime written = File.GetLastWriteTimeUtc(path);
            if (RelayNames.IsFileName(name) && written < cutoff)
            {
                _usedBytes -= new FileInfo(path).Length;
                File.Delete(path);
                _etags.Remove(path);
                removed++;
            }
            else if (name.Contains(TempMarker, StringComparison.Ordinal) && written < tempCutoff)
            {
                File.Delete(path);
            }
        }

        return removed;
    }

    private static void RemoveEmptyGroup(string directory, DateTime cutoff)
    {
        string ownerPath = Path.Combine(directory, OwnerFileName);
        bool hasFiles = Directory.EnumerateFiles(directory).Any(p => RelayNames.IsFileName(Path.GetFileName(p)));
        if (!hasFiles && File.Exists(ownerPath) && File.GetLastWriteTimeUtc(ownerPath) < cutoff)
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string GroupDirectory(string gid)
    {
        if (!RelayNames.IsGroupId(gid))
        {
            throw new ArgumentException("Not a relay group id.", nameof(gid));
        }

        return Path.Combine(_root, gid);
    }

    private static string FilePath(string directory, string name)
    {
        if (!RelayNames.IsFileName(name))
        {
            throw new ArgumentException("Not a sync file name.", nameof(name));
        }

        return Path.Combine(directory, name);
    }

    private static string? ReadOwner(string directory)
    {
        string path = Path.Combine(directory, OwnerFileName);
        return File.Exists(path) ? File.ReadAllText(path, Encoding.ASCII).Trim() : null;
    }

    private static bool OwnerMatches(string ownerHash, string tokenHash)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(ownerHash), Encoding.ASCII.GetBytes(tokenHash));
    }

    /// <summary>The bytes of all stored sync files, counted once at start; Put, Delete and the cleanup keep the total.</summary>
    private long MeasureStoredBytes()
    {
        return Directory.EnumerateDirectories(_root)
            .Where(d => RelayNames.IsGroupId(Path.GetFileName(d)))
            .SelectMany(Directory.EnumerateFiles)
            .Where(p => RelayNames.IsFileName(Path.GetFileName(p)))
            .Sum(p => new FileInfo(p).Length);
    }

    private int CountGroups()
    {
        return Directory.EnumerateDirectories(_root).Count(d => RelayNames.IsGroupId(Path.GetFileName(d)));
    }

    private static int CountFiles(string directory)
    {
        return Directory.EnumerateFiles(directory).Count(p => RelayNames.IsFileName(Path.GetFileName(p)));
    }

    private string ETagOf(FileInfo file)
    {
        if (_etags.TryGetValue(file.FullName, out CachedETag? cached)
            && cached.Length == file.Length
            && cached.WrittenTicks == file.LastWriteTimeUtc.Ticks)
        {
            return cached.ETag;
        }

        string etag = ComputeETag(File.ReadAllBytes(file.FullName));
        _etags[file.FullName] = new CachedETag(file.Length, file.LastWriteTimeUtc.Ticks, etag);
        return etag;
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        string temp = $"{path}{TempMarker}{Guid.NewGuid():N}";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            throw;
        }
    }

    private static string Base64Url(byte[] data)
    {
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed record CachedETag(long Length, long WrittenTicks, string ETag);
}
