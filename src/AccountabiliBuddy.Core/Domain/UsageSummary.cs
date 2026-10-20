namespace AccountabiliBuddy.Core.Domain;

/// <summary>Activity totals for a period, for the History tab. Times are summed per monitor.</summary>
/// <param name="FocusTime">All logged time during focus phases.</param>
/// <param name="TopDistractions">Apps/sites classified distracting during focus, most time first, one entry per site section.</param>
/// <param name="AppsAndSites">Time per app, and per site (and site section) within browsers, overall (in and out of sessions).</param>
public sealed record UsageSummary(
    TimeSpan FocusTime,
    TimeSpan OnTrackTime,
    TimeSpan DistractedTime,
    TimeSpan UnknownTime,
    IReadOnlyList<UsageEntry> TopDistractions,
    IReadOnlyList<AppSiteUsage> AppsAndSites)
{
    public static UsageSummary Empty { get; } = new(
        TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, Array.Empty<UsageEntry>(), Array.Empty<AppSiteUsage>());
}
