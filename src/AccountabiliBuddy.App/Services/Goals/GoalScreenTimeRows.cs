using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// Turns this PC's activity-history rows into <see cref="GoalAppRow"/>s and asks Core's <see cref="GoalAppMatching"/> whether a screen-time
/// entry counts them (ST2). One history row (process, site, site section) is one usage row and counts once however many entries match it.
/// A site row has two shown names, the program's and the site's, so an entry that names either one counts it. Pure; never logs.
/// </summary>
public static class GoalScreenTimeRows
{
    /// <summary>The usage rows <paramref name="usage"/> offers to <see cref="GoalAppMatching"/>: one with the program's shown name, and for a site row one more with the site's.</summary>
    public static IReadOnlyList<GoalAppRow> Variants(AppSiteUsage usage, Func<string, string> nameFor)
    {
        double minutes = usage.Time.TotalMinutes;
        var appRow = new GoalAppRow(usage.App, nameFor(usage.App), usage.Site, minutes);
        if (usage.Site is null)
        {
            return [appRow];
        }

        return [appRow, new GoalAppRow(usage.App, nameFor(usage.Site), usage.Site, minutes)];
    }

    /// <summary>Whether any of <paramref name="apps"/> counts <paramref name="usage"/> on a PC.</summary>
    public static bool Counts(IReadOnlyList<GoalAppMatch> apps, AppSiteUsage usage, Func<string, string> nameFor)
    {
        IReadOnlyList<GoalAppRow> variants = Variants(usage, nameFor);
        return apps.Any(match => variants.Any(row => GoalAppMatching.Matches(match, row, GoalAppPlatform.Pc)));
    }

    /// <summary>The whole minutes of <paramref name="rows"/> that any of <paramref name="apps"/> counts (each row once); sub-minute remainders are dropped, as every time source does.</summary>
    public static double Minutes(IReadOnlyList<GoalAppMatch> apps, IReadOnlyList<AppSiteUsage> rows, Func<string, string> nameFor)
    {
        if (apps.Count == 0)
        {
            return 0;
        }

        double minutes = rows.Where(row => Counts(apps, row, nameFor)).Sum(row => row.Time.TotalMinutes);
        return Math.Floor(minutes + 1e-9);
    }
}
