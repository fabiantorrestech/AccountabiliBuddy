using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Activity no rule covers that the user hasn't classified within the profile's grace period. It escalates like a
/// blocked view (notification, then overlay), but never past the overlay: it isn't known to be distracting, so it is
/// neither minimized nor sent to the block page. Answering the classify prompt ends it.
/// </summary>
public static class UnknownActivity
{
    /// <summary>The built-in rule these escalations are reported under. Not part of any profile; never stored.</summary>
    public static Rule Rule { get; } = new(
        new Guid("b1d6a4e2-93c7-4f0b-8e25-6a7d0c4f9e38"),
        MatchTarget.WindowTitle,
        MatchKind.Contains,
        string.Empty,
        RuleVerdict.Block,
        "Not classified yet");

    public static bool IsRule(Rule? rule)
    {
        return rule?.Id == Rule.Id;
    }
}
