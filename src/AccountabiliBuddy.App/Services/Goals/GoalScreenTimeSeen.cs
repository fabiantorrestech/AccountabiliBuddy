using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// What this PC saw in its own activity history over the last 30 days (ST2): the editor's pick list and its "Not seen on this device yet"
/// note. Local only: names stay on this PC, in memory, for as long as the dialog is open; nothing here is stored, logged or sent.
/// </summary>
public sealed class GoalScreenTimeSeen
{
    /// <summary>The days read for the pick list.</summary>
    public const int Days = 30;

    /// <summary>The most things offered (most time first).</summary>
    public const int MostItems = 60;

    private readonly IReadOnlyList<AppSiteUsage> _usage;
    private readonly Func<string, string> _nameFor;

    public GoalScreenTimeSeen(IReadOnlyList<AppSiteUsage> usage, Func<string, string> nameFor, Func<string, bool>? isHidden = null)
    {
        _usage = usage;
        _nameFor = nameFor;
        Items = BuildItems(usage, nameFor, isHidden ?? (_ => false));
    }

    public static GoalScreenTimeSeen Empty { get; } = new([], name => name);

    /// <summary>Programs (as a <see cref="GoalAppMatchKind.Process"/> entry) and sites (<see cref="GoalAppMatchKind.Website"/>), most time first.</summary>
    public IReadOnlyList<GoalScreenTimeSeenItem> Items { get; }

    /// <summary>Whether any usage row of this PC's last 30 days is counted by <paramref name="match"/> on a PC.</summary>
    public bool WasSeen(GoalAppMatch match)
    {
        return _usage.Any(row => GoalScreenTimeRows.Counts([match], row, _nameFor));
    }

    private static IReadOnlyList<GoalScreenTimeSeenItem> BuildItems(IReadOnlyList<AppSiteUsage> usage, Func<string, string> nameFor, Func<string, bool> isHidden)
    {
        var items = new List<GoalScreenTimeSeenItem>();
        foreach (IGrouping<string, AppSiteUsage> app in usage.Where(row => !isHidden(row.App)).GroupBy(row => row.App, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new GoalScreenTimeSeenItem(
                new GoalAppMatch(GoalAppMatchKind.Process, app.Key), nameFor(app.Key), isSite: false, app.Sum(row => row.Time.TotalMinutes)));
        }

        // A site's time is counted whatever browser showed it; only real-looking domains are offered (a page without one is not a site).
        foreach (IGrouping<string, AppSiteUsage> site in usage
                     .Where(row => row.Site is { } domain && domain.Contains('.') && !domain.Any(char.IsWhiteSpace) && !isHidden(domain))
                     .GroupBy(row => row.Site!, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new GoalScreenTimeSeenItem(
                new GoalAppMatch(GoalAppMatchKind.Website, site.Key), nameFor(site.Key), isSite: true, site.Sum(row => row.Time.TotalMinutes)));
        }

        return items.OrderByDescending(item => item.Minutes).Take(MostItems).ToList();
    }
}
