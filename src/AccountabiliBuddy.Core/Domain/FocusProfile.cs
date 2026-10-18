using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// Defines what "on track" means for a session, e.g. a "Coding" profile.
/// Activity matching no rule is Unknown; after <see cref="UnknownGracePeriod"/>
/// the user is asked to classify it. <see cref="UnlockOptions"/> are the ways to lift its blocks (schema v2).
/// </summary>
/// <remarks>
/// <see cref="AskAboutUnlistedSites"/> (schema v3, on by default): a website no rule names counts as Unknown even
/// when an app rule allows the browser, so "the browser is allowed" doesn't allow every site.
/// </remarks>
public sealed record FocusProfile(
    Guid Id,
    string Name,
    IReadOnlyList<Rule> Rules,
    TimeSpan UnknownGracePeriod)
{
    public IReadOnlyList<UnlockOption> UnlockOptions { get; init; } = Array.Empty<UnlockOption>();

    public bool AskAboutUnlistedSites { get; init; } = true;

    // Derived from Rules; not part of the shared JSON schema.
    [JsonIgnore]
    public bool HasOcrRules => Rules.Any(rule => rule.Target == MatchTarget.OcrText);
}
