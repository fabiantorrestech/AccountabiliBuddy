using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Mail and chat (comms windows, schema v6): desktop apps by process name and their web versions by host. On a day whose
/// plan has Comms parts, these are on track inside them and distracting during a session outside them.
/// </summary>
public static class CommsApps
{
    // Fixed ids for the stand-in rules, so a repeat escalation is remembered like any rule's.
    private static readonly Guid AppRuleId = Guid.Parse("c0331e5a-0001-4f0e-9d1b-7a5c00000001");
    private static readonly Guid SiteRuleId = Guid.Parse("c0331e5a-0002-4f0e-9d1b-7a5c00000002");

    public static IReadOnlyList<string> Processes { get; } = ["outlook", "olk", "teams", "ms-teams", "slack", "thunderbird"];

    public static IReadOnlyList<string> Hosts { get; } = ["mail.google.com", "outlook.office.com", "outlook.live.com", "teams.microsoft.com", "app.slack.com"];

    public static bool IsComms(ActivitySnapshot snapshot)
    {
        return Host(snapshot) is not null || Processes.Contains(snapshot.ProcessName.ToLowerInvariant());
    }

    /// <summary>The block rule a comms window outside its time stands in for (the site for a browser tab, else the app).</summary>
    public static Rule RuleFor(ActivitySnapshot snapshot)
    {
        return Host(snapshot) is string host
            ? new Rule(SiteRuleId, MatchTarget.Url, MatchKind.Domain, host, RuleVerdict.Block, "Outside a comms window")
            : new Rule(AppRuleId, MatchTarget.ProcessName, MatchKind.Exact, snapshot.ProcessName.ToLowerInvariant(), RuleVerdict.Block, "Outside a comms window");
    }

    private static string? Host(ActivitySnapshot snapshot)
    {
        string? host = snapshot.Url is null ? null : UrlNormalizer.GetHost(snapshot.Url)?.ToLowerInvariant();
        return host is not null && Hosts.Contains(host) ? host : null;
    }
}
