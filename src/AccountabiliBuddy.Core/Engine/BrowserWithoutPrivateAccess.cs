using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// A watched browser whose extension isn't allowed in private windows can't be watched reliably, so during focus
/// all its windows count as distracting (the whole app, so it gets minimized) until the user allows it. The
/// browser's own settings pages stay usable, so the setting can be turned on (see <see cref="BrowserTabMatcher"/>).
/// </summary>
public static class BrowserWithoutPrivateAccess
{
    /// <summary>The built-in rule these windows are reported under. Not part of any profile; never stored.</summary>
    public static Rule Rule { get; } = new(
        new Guid("3d9e5a71-8c2b-4e60-b1f4-7a0c9d2e6b48"),
        MatchTarget.ProcessName,
        MatchKind.Exact,
        string.Empty,
        RuleVerdict.Block,
        "Browser without AccountabiliBuddy in private windows");

    public static bool IsRule(Rule? rule)
    {
        return rule?.Id == Rule.Id;
    }

    /// <summary>Overrides the rule-based result for windows of such a browser.</summary>
    public static ClassificationResult Apply(ActivitySnapshot snapshot, ClassificationResult result)
    {
        return snapshot.LacksPrivateWindowAccess ? ClassificationResult.FromRule(Rule) : result;
    }
}
