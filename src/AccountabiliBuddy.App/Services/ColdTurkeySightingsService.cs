using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Notes which sites Cold Turkey blocks, by watching for its block page in the tabs our extension reports.
/// Purely observational: Cold Turkey itself is never queried or modified. Each block page is counted once per tab.
/// The list is saved locally so it survives restarts. Use from the UI thread only.
/// </summary>
public sealed class ColdTurkeySightingsService
{
    private const string DocumentKey = "integrations.coldTurkey.sightings";
    private const int MaxRememberedPages = 500;

    private readonly IBrowserActivitySource _browsers;
    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _timeProvider;

    private readonly Dictionary<string, BlockedSiteSighting> _bySite = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _countedPages = new(StringComparer.Ordinal);

    public ColdTurkeySightingsService(IBrowserActivitySource browsers, SqliteJsonDocumentStore store, TimeProvider timeProvider)
    {
        _browsers = browsers;
        _store = store;
        _timeProvider = timeProvider;
        _browsers.ConnectionsChanged += (_, _) => Dispatcher.UIThread.Post(Inspect);
    }

    /// <summary>Most recently seen first.</summary>
    public IReadOnlyList<BlockedSiteSighting> Sightings =>
        _bySite.Values.OrderByDescending(sighting => sighting.LastSeen).ToList();

    /// <summary>True once a Cold Turkey block page was seen whose address does not say which site it blocked.</summary>
    public bool SawBlockPageWithoutSite { get; private set; }

    public event EventHandler? Changed;

    public async Task LoadAsync()
    {
        try
        {
            List<BlockedSiteSighting>? saved = await _store.LoadAsync<List<BlockedSiteSighting>>(DocumentKey);
            foreach (BlockedSiteSighting sighting in saved ?? new List<BlockedSiteSighting>())
            {
                _bySite[sighting.Site] = sighting;
            }
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            // Start with an empty list; it is only a convenience.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Forget(string site)
    {
        if (_bySite.Remove(site))
        {
            Changed?.Invoke(this, EventArgs.Empty);
            _ = SaveAsync();
        }
    }

    private void Inspect()
    {
        bool changed = false;
        DateTimeOffset now = _timeProvider.GetUtcNow();

        foreach (BrowserActivity tab in _browsers.Connections.SelectMany(connection => connection.ActiveTabs))
        {
            if (!ColdTurkeyBlockPage.IsBlockPage(tab.Url, tab.Title) || !_countedPages.Add($"{tab.TabId}|{tab.Url}"))
            {
                continue;
            }

            string? site = ColdTurkeyBlockPage.TryGetBlockedSite(tab.Url);
            if (site is null)
            {
                changed |= !SawBlockPageWithoutSite;
                SawBlockPageWithoutSite = true;
                continue;
            }

            _bySite[site] = _bySite.TryGetValue(site, out BlockedSiteSighting? previous)
                ? previous with { LastSeen = now, TimesSeen = previous.TimesSeen + 1 }
                : new BlockedSiteSighting(site, now, now, 1);
            changed = true;
        }

        if (_countedPages.Count > MaxRememberedPages)
        {
            _countedPages.Clear();
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            _ = SaveAsync();
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveAsync(DocumentKey, _bySite.Values.ToList());
        }
        catch (SqliteException)
        {
            // Not critical; the next change tries again.
        }
    }
}
