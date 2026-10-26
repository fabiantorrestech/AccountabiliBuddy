using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Import;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Which Cold Turkey blocks are on (checked every minute and when a session changes; Cold Turkey's file is read again
/// only when it changed), and which ones each profile expects (documents "coldturkey.expected", this computer only).
/// During focus, expected blocks that are off are named once through <see cref="ExpectedBlocksOff"/>: at the start,
/// and again for a block that turns off later. Awareness only here; ColdTurkeyKeysService locks and switches blocks through its command line (ADR 39).
/// Use from the UI thread only.
/// </summary>
public sealed class ColdTurkeyService : IDisposable
{
    private const string DocumentKey = "coldturkey.expected";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private static readonly IReadOnlySet<string> NoBlocks = new HashSet<string>(StringComparer.Ordinal);

    private readonly IColdTurkeyBlockReader _reader;
    private readonly SessionController _sessions;
    private readonly SqliteJsonDocumentStore _store;
    private readonly DispatcherTimer _timer;

    // Expected blocks already named this session; one that comes back on is named again if it goes off.
    private readonly HashSet<string> _named = new(StringComparer.Ordinal);

    public ColdTurkeyService(IColdTurkeyBlockReader reader, SessionController sessions, SqliteJsonDocumentStore store)
    {
        _reader = reader;
        _sessions = sessions;
        _store = store;
        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += (_, _) => Refresh();
        _sessions.StateChanged += (_, _) => Refresh();
    }

    /// <summary>Null when Cold Turkey isn't installed or its settings can't be read.</summary>
    public ColdTurkeyBlocks? Blocks { get; private set; }

    /// <summary>Names of the blocks blocking right now.</summary>
    public IReadOnlySet<string> OnNow { get; private set; } = NoBlocks;

    /// <summary>Profile id → the Cold Turkey blocks it expects to be on during its focus.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> Expected { get; private set; } = new Dictionary<Guid, IReadOnlyList<string>>();

    /// <summary>Raised when the blocks, what's on, or the expected blocks changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised during focus with expected blocks that are off (each named once, see the class summary).</summary>
    public event EventHandler<IReadOnlyList<string>>? ExpectedBlocksOff;

    public void LoadBlocking()
    {
        try
        {
            Dictionary<Guid, List<string>>? saved = Task.Run(() => _store.LoadAsync<Dictionary<Guid, List<string>>>(DocumentKey))
                .GetAwaiter().GetResult();
            Expected = saved?.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)entry.Value)
                       ?? new Dictionary<Guid, IReadOnlyList<string>>();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Expected = new Dictionary<Guid, IReadOnlyList<string>>();
        }
    }

    public void Start()
    {
        Refresh();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    public IReadOnlyList<string> ExpectedFor(Guid profileId)
    {
        return Expected.TryGetValue(profileId, out IReadOnlyList<string>? names) ? names : Array.Empty<string>();
    }

    /// <summary>Saves every profile's expected blocks (profiles expecting none are left out).</summary>
    public async Task SaveExpectedAsync(IReadOnlyDictionary<Guid, IReadOnlyList<string>> expected)
    {
        Dictionary<Guid, List<string>> kept = expected
            .Where(entry => entry.Value.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value.ToList());
        Expected = kept.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)entry.Value);
        await _store.SaveAsync(DocumentKey, kept);
        Changed?.Invoke(this, EventArgs.Empty);
        NameExpectedBlocksOff();
    }

    /// <summary>E.g. "'Amazon / Online Shopping' is off in Cold Turkey, and this profile expects it on."</summary>
    public static string Describe(IReadOnlyList<string> names)
    {
        string listed = names.Count == 1
            ? $"'{names[0]}' is"
            : string.Join(", ", names.Take(names.Count - 1).Select(name => $"'{name}'")) + $" and '{names[^1]}' are";
        string them = names.Count == 1 ? "it" : "them";
        return $"{listed} off in Cold Turkey, and this profile expects {them} on.";
    }

    /// <summary>Reads Cold Turkey's settings again now, e.g. after a command (ADR 39).</summary>
    public void RefreshNow() => Refresh();

    private void Refresh()
    {
        ColdTurkeyBlocks? blocks = _reader.Read();
        DateTime localNow = TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        IReadOnlySet<string> onNow = blocks?.OnAt(localNow) ?? NoBlocks;

        // The reader hands back the same object while Cold Turkey's file is unchanged.
        bool changed = !ReferenceEquals(blocks, Blocks) || !onNow.SetEquals(OnNow);
        Blocks = blocks;
        OnNow = onNow;
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        NameExpectedBlocksOff();
    }

    private void NameExpectedBlocksOff()
    {
        SessionState state = _sessions.State;
        if (!state.IsActive)
        {
            // The next session names them again.
            _named.Clear();
            return;
        }

        // Breaks don't count, and settings that can't be read say nothing about what's off.
        if (!state.IsEnforcing || state.ProfileId is not Guid profileId || Blocks is null)
        {
            return;
        }

        List<string> off = ExpectedFor(profileId).Where(name => !OnNow.Contains(name)).ToList();
        _named.IntersectWith(off);
        List<string> newlyOff = off.Where(name => !_named.Contains(name)).ToList();
        if (newlyOff.Count == 0)
        {
            return;
        }

        _named.UnionWith(newlyOff);
        ExpectedBlocksOff?.Invoke(this, newlyOff);
    }
}
