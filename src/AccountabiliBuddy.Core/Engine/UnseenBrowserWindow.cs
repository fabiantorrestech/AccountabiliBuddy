using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// A window of a watched browser that the extension did not report: a private window the extension isn't allowed
/// in, a browser profile without the extension, or the extension turned off. It could be showing anything, so during
/// focus it is treated like a blocked view. Only rules about what is visible without the extension (window title,
/// screen text) can still decide it; process rules ("chrome is allowed") can't, or every private window would pass.
/// </summary>
public static class UnseenBrowserWindow
{
    /// <summary>The built-in rule these windows are reported under. Not part of any profile; never stored.</summary>
    public static Rule Rule { get; } = new(
        new Guid("6f1c2b0e-4f7a-4d38-9a51-2f0f8c7e5b11"),
        MatchTarget.Url,
        MatchKind.Contains,
        string.Empty,
        RuleVerdict.Block,
        "Browser window AccountabiliBuddy can't see");

    public static bool IsRule(Rule? rule)
    {
        return rule?.Id == Rule.Id;
    }

    /// <summary>Applies the policy above to the rule-based result for <paramref name="snapshot"/>.</summary>
    public static ClassificationResult Apply(ActivitySnapshot snapshot, ClassificationResult result)
    {
        if (!snapshot.IsUnseenBrowserWindow
            || result.MatchedRule?.Target is MatchTarget.WindowTitle or MatchTarget.OcrText)
        {
            return result;
        }

        return ClassificationResult.FromRule(Rule);
    }
}
