using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Classifies a snapshot against a profile.
/// When several rules match, the most specific wins:
/// 1. URL beats window title, which beats process name.
/// 2. A longer pattern beats a shorter one.
/// 3. On a tie, Block beats Allow.
/// OCR rules are only consulted when no other rule matched (detection cascade).
/// </summary>
public sealed class RuleEvaluator
{
    private readonly RuleMatcher _matcher;

    public RuleEvaluator(RuleMatcher matcher)
    {
        _matcher = matcher;
    }

    public ClassificationResult Evaluate(FocusProfile profile, ActivitySnapshot snapshot)
    {
        Rule? primaryMatch = FindMostSpecificMatch(
            profile.Rules.Where(rule => rule.Target != MatchTarget.OcrText),
            snapshot);

        if (primaryMatch is not null)
        {
            return ClassificationResult.FromRule(primaryMatch);
        }

        if (snapshot.OcrText is null)
        {
            return ClassificationResult.Unknown;
        }

        Rule? ocrMatch = FindMostSpecificMatch(
            profile.Rules.Where(rule => rule.Target == MatchTarget.OcrText),
            snapshot);

        return ocrMatch is null ? ClassificationResult.Unknown : ClassificationResult.FromRule(ocrMatch);
    }

    private Rule? FindMostSpecificMatch(IEnumerable<Rule> rules, ActivitySnapshot snapshot)
    {
        Rule? best = null;

        foreach (Rule rule in rules)
        {
            if (!_matcher.Matches(rule, snapshot))
            {
                continue;
            }

            if (best is null || IsMoreSpecific(rule, best))
            {
                best = rule;
            }
        }

        return best;
    }

    private static bool IsMoreSpecific(Rule candidate, Rule current)
    {
        int targetComparison = TargetRank(candidate.Target).CompareTo(TargetRank(current.Target));
        if (targetComparison != 0)
        {
            return targetComparison > 0;
        }

        int lengthComparison = candidate.Pattern.Length.CompareTo(current.Pattern.Length);
        if (lengthComparison != 0)
        {
            return lengthComparison > 0;
        }

        return candidate.Verdict == RuleVerdict.Block && current.Verdict == RuleVerdict.Allow;
    }

    private static int TargetRank(MatchTarget target)
    {
        return target switch
        {
            MatchTarget.Url => 3,
            MatchTarget.WindowTitle => 2,
            MatchTarget.ProcessName => 1,
            _ => 0,
        };
    }
}
