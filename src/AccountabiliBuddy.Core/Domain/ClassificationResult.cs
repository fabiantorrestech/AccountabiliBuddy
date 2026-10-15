namespace AccountabiliBuddy.Core.Domain;

/// <summary>Outcome of evaluating a snapshot against a profile, with the rule that decided it.</summary>
public sealed record ClassificationResult(Classification Classification, Rule? MatchedRule)
{
    public static ClassificationResult Unknown { get; } = new(Classification.Unknown, null);

    public static ClassificationResult FromRule(Rule rule)
    {
        Classification classification = rule.Verdict == RuleVerdict.Allow
            ? Classification.OnTrack
            : Classification.Distracting;
        return new ClassificationResult(classification, rule);
    }
}
