using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Link;

/// <summary>
/// "Phone link paused on &lt;network&gt; (Public)": told once per network, only while a device is paired, and
/// remembered ("devices.publicNetworksTold"), so a network set to Public on purpose doesn't nag. Use from the UI
/// thread only.
/// </summary>
public sealed class PublicNetworkNoticeService
{
    private const string DocumentKey = "devices.publicNetworksTold";

    private readonly SqliteJsonDocumentStore _store;
    private HashSet<string> _told = new(StringComparer.Ordinal);

    public PublicNetworkNoticeService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public void LoadBlocking()
    {
        try
        {
            IReadOnlyList<string>? names = Task.Run(() => _store.LoadAsync<Told>(DocumentKey)).GetAwaiter().GetResult()?.Networks;
            _told = new HashSet<string>(names ?? [], StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _told = new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>True the first time for <paramref name="networkName"/> while a device is paired; that network is then remembered.</summary>
    public async Task<bool> ShouldTellAsync(string networkName, bool anyDevicePaired)
    {
        if (!anyDevicePaired || !_told.Add(networkName))
        {
            return false;
        }

        await _store.SaveAsync(DocumentKey, new Told(_told.Order(StringComparer.Ordinal).ToList()));
        return true;
    }

    private sealed record Told(IReadOnlyList<string> Networks);
}
