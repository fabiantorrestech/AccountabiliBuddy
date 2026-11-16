namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Which usage rows a screen-time source's entries count, per kind of device. Twin: Kotlin <c>GoalAppMatching</c>. Pure: the
/// caller reads the rows and decides which devices count (<see cref="CountsOn"/>).
/// </summary>
public static class GoalAppMatching
{
    private const string ExeSuffix = ".exe";
    private const string WwwPrefix = "www.";

    public static bool Matches(GoalAppMatch match, GoalAppRow row, GoalAppPlatform platform)
    {
        bool phone = platform == GoalAppPlatform.Phone;
        switch (match.Kind)
        {
            case GoalAppMatchKind.Package:
                return phone && string.Equals(row.Id, match.Text, StringComparison.Ordinal);

            case GoalAppMatchKind.Process:
                return !phone && string.Equals(WithoutExe(row.Id), WithoutExe(match.Text), StringComparison.OrdinalIgnoreCase);

            case GoalAppMatchKind.Website:
                if (phone || row.Site is null)
                {
                    return false;
                }

                string site = WithoutWww(row.Site);
                string domain = WithoutWww(match.Text);
                return domain.Length > 0
                    && (site.Equals(domain, StringComparison.OrdinalIgnoreCase) || site.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

            case GoalAppMatchKind.Name:
                return (row.Label is not null && string.Equals(row.Label, match.Text, StringComparison.OrdinalIgnoreCase))
                    || (!phone && string.Equals(WithoutExe(row.Id), match.Text, StringComparison.OrdinalIgnoreCase));

            default:
                return false;
        }
    }

    /// <summary>The minutes of the <paramref name="rows"/> that any entry of <paramref name="rule"/> matches; a row counts once however many entries hit it.</summary>
    public static double MinutesFor(GoalSourceRule rule, IEnumerable<GoalAppRow> rows, GoalAppPlatform platform)
    {
        if (rule.Apps is not { Count: > 0 } apps)
        {
            return 0;
        }

        return rows.Where(row => apps.Any(match => Matches(match, row, platform))).Sum(row => row.Minutes);
    }

    /// <summary>
    /// Whether <paramref name="deviceId"/> may count <paramref name="rule"/>: every device when <see cref="GoalSourceRule.AllDevices"/>,
    /// else the listed <see cref="GoalSourceRule.Devices"/>, else (none listed) only the device that created it
    /// (<see cref="GoalSourceRule.CreatedOn"/>; a rule written before devices were recorded counts wherever it is read).
    /// </summary>
    public static bool CountsOn(GoalSourceRule rule, Guid deviceId)
    {
        if (rule.AllDevices)
        {
            return true;
        }

        if (rule.Devices is not null)
        {
            return rule.Devices.Contains(deviceId);
        }

        return rule.CreatedOn is null || rule.CreatedOn == deviceId;
    }

    /// <summary>
    /// ST4: whether <paramref name="match"/> counts on <paramref name="deviceId"/>: the rule counts there (<see cref="CountsOn(GoalSourceRule, Guid)"/>)
    /// and the entry lists that device or lists none (<see cref="GoalAppMatch.Devices"/> null; an empty list counts nowhere).
    /// </summary>
    public static bool CountsOn(GoalSourceRule rule, GoalAppMatch match, Guid deviceId)
    {
        return CountsOn(rule, deviceId) && (match.Devices is null || match.Devices.Contains(deviceId));
    }

    /// <summary>
    /// ST4: the minutes of the <paramref name="rows"/> that the entries of <paramref name="rule"/> counting on <paramref name="deviceId"/> match
    /// (the entries are filtered first, then a row counts once however many of them hit it). 0 when the rule doesn't count on the device.
    /// </summary>
    public static double MinutesFor(GoalSourceRule rule, IEnumerable<GoalAppRow> rows, GoalAppPlatform platform, Guid deviceId)
    {
        if (rule.Apps is not { Count: > 0 } apps)
        {
            return 0;
        }

        List<GoalAppMatch> counted = apps.Where(match => CountsOn(rule, match, deviceId)).ToList();
        return counted.Count == 0 ? 0 : rows.Where(row => counted.Any(match => Matches(match, row, platform))).Sum(row => row.Minutes);
    }

    /// <summary>
    /// ST4, plan check B7: the entries with one entry per kind and text (compared as <c>GoalUnknowns</c> keys an entry: the kind, then the
    /// exact text). The first twin keeps its place and its unknown fields; the twins' device lists are joined (in the order met, no repeats),
    /// and a twin with no list (every device) makes the entry count everywhere. The same list is returned when there is nothing to merge.
    /// </summary>
    public static IReadOnlyList<GoalAppMatch> OneEntryPerKindAndText(IReadOnlyList<GoalAppMatch> entries)
    {
        if (entries.Select(entry => (entry.Kind, entry.Text)).Distinct().Count() == entries.Count)
        {
            return entries;
        }

        var merged = new List<GoalAppMatch>();
        foreach (GoalAppMatch entry in entries)
        {
            int at = merged.FindIndex(held => held.Kind == entry.Kind && held.Text == entry.Text);
            if (at < 0)
            {
                merged.Add(entry);
                continue;
            }

            GoalAppMatch first = merged[at];
            IReadOnlyList<Guid>? devices = first.Devices is null || entry.Devices is null ? null : first.Devices.Concat(entry.Devices).Distinct().ToList();
            merged[at] = first with { Devices = devices };
        }

        return merged;
    }

    /// <summary>ST4: whether <paramref name="match"/> names devices and none of them is among <paramref name="countedDevices"/> (the editor warns "Counts on none of the ticked devices").</summary>
    public static bool CountsNowhere(GoalAppMatch match, IReadOnlyCollection<Guid> countedDevices)
    {
        return match.Devices is { } devices && !devices.Any(countedDevices.Contains);
    }

    private static string WithoutExe(string text)
    {
        return text.EndsWith(ExeSuffix, StringComparison.OrdinalIgnoreCase) ? text[..^ExeSuffix.Length] : text;
    }

    private static string WithoutWww(string text)
    {
        return text.StartsWith(WwwPrefix, StringComparison.OrdinalIgnoreCase) ? text[WwwPrefix.Length..] : text;
    }
}
