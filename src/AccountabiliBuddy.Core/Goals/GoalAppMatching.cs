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

    private static string WithoutExe(string text)
    {
        return text.EndsWith(ExeSuffix, StringComparison.OrdinalIgnoreCase) ? text[..^ExeSuffix.Length] : text;
    }

    private static string WithoutWww(string text)
    {
        return text.StartsWith(WwwPrefix, StringComparison.OrdinalIgnoreCase) ? text[WwwPrefix.Length..] : text;
    }
}
