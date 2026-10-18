using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// Defines what "on track" means for a session, e.g. a "Coding" profile.
/// Activity matching no rule is Unknown; after <see cref="UnknownGracePeriod"/>
/// the user is asked to classify it. <see cref="UnlockOptions"/> are the ways to lift its blocks (schema v2).
/// </summary>
public sealed record FocusProfile(
    Guid Id,
    string Name,
    IReadOnlyList<Rule> Rules,
    TimeSpan UnknownGracePeriod)
{
    public IReadOnlyList<UnlockOption> UnlockOptions { get; init; } = Array.Empty<UnlockOption>();

    // Derived from Rules; not part of the shared JSON schema.
    [JsonIgnore]
    public bool HasOcrRules => Rules.Any(rule => rule.Target == MatchTarget.OcrText);
}
