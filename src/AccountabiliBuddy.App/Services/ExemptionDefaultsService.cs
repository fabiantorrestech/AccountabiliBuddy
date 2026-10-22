using System.Text.Json;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The profiles ticked from the start in the classify prompt's "Add exemption" (Profiles tab, this computer only), so
/// an exemption goes to them too without ticking them each time. App-owned document "classify.exemptionProfiles".
/// </summary>
public sealed class ExemptionDefaultsService
{
    private const string DocumentKey = "classify.exemptionProfiles";

    private readonly SqliteJsonDocumentStore _store;

    public ExemptionDefaultsService(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    public IReadOnlySet<Guid> PreTicked { get; private set; } = new HashSet<Guid>();

    public void LoadBlocking()
    {
        try
        {
            List<Guid>? saved = Task.Run(() => _store.LoadAsync<List<Guid>>(DocumentKey)).GetAwaiter().GetResult();
            PreTicked = (saved ?? []).ToHashSet();
        }
        catch (Exception ex) when (ex is SqliteException or JsonException)
        {
            PreTicked = new HashSet<Guid>();
        }
    }

    public async Task SaveAsync(IEnumerable<Guid> profileIds)
    {
        var ids = profileIds.ToList();
        PreTicked = ids.ToHashSet();
        await _store.SaveAsync(DocumentKey, ids);
    }
}
