using System.Text.Json;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Today;

/// <summary>
/// Keeps <see cref="TodayLayoutPreferences"/> in the local document "today.layout" (this PC only, never synced; a device setting in
/// backups, restored through <see cref="RestoreAsync"/>). Read once at start; every change is saved and announced at once: it only
/// changes what Today draws, so it is not locked during a session. The one rule it holds: outside ProductaBuddy the lunch part can't
/// be turned off (<see cref="TodayLayoutRules.CanTurnOff"/>). Use from the UI thread.
/// </summary>
public sealed class TodayLayoutService
{
    public const string DocumentKey = "today.layout";

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteJsonDocumentStore _documents;
    private readonly Func<bool> _isProductaBuddy;

    /// <param name="isProductaBuddy">Whether the app runs in ProductaBuddy mode now (ADR 68).</param>
    public TodayLayoutService(SqliteJsonDocumentStore documents, Func<bool> isProductaBuddy)
    {
        _documents = documents;
        _isProductaBuddy = isProductaBuddy;
    }

    public TodayLayoutPreferences Current { get; private set; } = TodayLayoutPreferences.Default;

    public bool IsProductaBuddy => _isProductaBuddy();

    /// <summary>The layout changed (on the UI thread, before it is saved).</summary>
    public event EventHandler? Changed;

    /// <summary>Reads the saved layout; an unreadable one leaves the defaults (a bad preference must never stop the app starting).</summary>
    public void LoadBlocking()
    {
        try
        {
            Current = Task.Run(() => _documents.LoadAsync<TodayLayoutPreferences>(DocumentKey)).GetAwaiter().GetResult() ?? TodayLayoutPreferences.Default;
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or InvalidOperationException or NotSupportedException)
        {
            Current = TodayLayoutPreferences.Default;
        }
    }

    /// <summary>Every sidebar part once, in order, with its chosen place, for a state of the day.</summary>
    public IReadOnlyList<TodayPartChoice> Parts(bool inSession) => TodayLayoutRules.Resolve(Current.StoredFor(inSession));

    /// <summary>Where a part is drawn now (the guard rule applied).</summary>
    public TodayPartPlacement EffectivePlacement(TodayPartChoice choice)
    {
        return TodayParts.ByKey(choice.Key) is { } info ? TodayLayoutRules.Effective(info, choice.Placement, IsProductaBuddy) : TodayPartPlacement.Off;
    }

    /// <summary>Puts a part at a place; false (nothing changes) for Off on the lunch part outside ProductaBuddy, or an unknown part.</summary>
    public async Task<bool> SetPlacementAsync(bool inSession, string key, TodayPartPlacement placement)
    {
        IReadOnlyList<TodayPartChoice> parts = Parts(inSession);
        IReadOnlyList<TodayPartChoice> next = TodayLayoutRules.WithPlacement(parts, key, placement, IsProductaBuddy);
        if (ReferenceEquals(next, parts))
        {
            return false;
        }

        await UpdateAsync(current => current.WithParts(inSession, next));
        return true;
    }

    /// <summary>Moves a part to a place in the order (drag ≡ or Alt+↑/↓).</summary>
    public Task MoveAsync(bool inSession, string key, int index)
    {
        IReadOnlyList<TodayPartChoice> parts = Parts(inSession);
        IReadOnlyList<TodayPartChoice> next = TodayLayoutRules.Move(parts, key, index);
        return ReferenceEquals(next, parts) ? Task.CompletedTask : UpdateAsync(current => current.WithParts(inSession, next));
    }

    public Task SetSidebarInSessionAsync(TodaySidebarInSession choice) => UpdateAsync(current => current with { SidebarInSession = choice });

    public Task SetSidebarOutsideAsync(TodaySidebarOutside choice) => UpdateAsync(current => current with { SidebarOutside = choice });

    public Task SetShowFinishedTodayAsync(bool show) => UpdateAsync(current => current with { ShowFinishedToday = show });

    /// <summary>Back to the board's layout in both states.</summary>
    public Task ResetAsync() => UpdateAsync(_ => TodayLayoutPreferences.Default);

    /// <summary>
    /// A backup's "today.layout" (ADR 71 device settings): read safely and taken over through this service, so the page redraws and
    /// the copy in memory isn't saved over it. Unknown parts are dropped; the guard rule still applies when it is drawn.
    /// </summary>
    public async Task<string?> RestoreAsync(JsonElement value)
    {
        TodayLayoutPreferences? wanted;
        try
        {
            wanted = value.Deserialize<TodayLayoutPreferences>(ReadOptions);
        }
        catch (JsonException)
        {
            return "Today's layout in the backup couldn't be read; this PC's stays.";
        }

        if (wanted is null)
        {
            return null;
        }

        TodayLayoutPreferences cleaned = wanted with
        {
            OutsideSession = wanted.OutsideSession is null ? null : TodayLayoutRules.Resolve(wanted.OutsideSession),
            InSession = wanted.InSession is null ? null : TodayLayoutRules.Resolve(wanted.InSession),
            SidebarInSession = Enum.IsDefined(wanted.SidebarInSession) ? wanted.SidebarInSession : TodaySidebarInSession.Hidden,
            SidebarOutside = Enum.IsDefined(wanted.SidebarOutside) ? wanted.SidebarOutside : TodaySidebarOutside.Remember,
        };
        await UpdateAsync(_ => cleaned);
        return null;
    }

    private async Task UpdateAsync(Func<TodayLayoutPreferences, TodayLayoutPreferences> change)
    {
        TodayLayoutPreferences next = change(Current);
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
            // Not remembered this time; Today keeps it until the app closes.
        }
    }
}
