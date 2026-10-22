using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>Copying rules from one profile into another (Profiles tab: tick rules, then Copy to or Move to).</summary>
public static class RuleTransfer
{
    /// <summary>
    /// The same rule for copying: what it matches on, how, its pattern (any case, spaces at the ends ignored) and its
    /// verdict. The note doesn't count.
    /// </summary>
    public static bool IsSame(Rule first, Rule second)
    {
        return first.Target == second.Target
               && first.Kind == second.Kind
               && first.Verdict == second.Verdict
               && string.Equals(first.Pattern.Trim(), second.Pattern.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What copying <paramref name="incoming"/> into a profile that has <paramref name="existing"/> adds: copies with new
    /// ids (notes kept), leaving out rules it already has, repeats among the incoming ones and rules with no pattern.
    /// </summary>
    /// <returns>The copies to add, and how many were left out because the profile already had them.</returns>
    public static (IReadOnlyList<Rule> Added, int AlreadyThere) CopyInto(IEnumerable<Rule> existing, IEnumerable<Rule> incoming)
    {
        var kept = existing.ToList();
        var added = new List<Rule>();
        int alreadyThere = 0;
        foreach (Rule rule in incoming.Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern)))
        {
            if (kept.Any(other => IsSame(other, rule)))
            {
                alreadyThere += existing.Any(other => IsSame(other, rule)) ? 1 : 0;
                continue;
            }

            Rule copy = rule with { Id = Guid.NewGuid() };
            kept.Add(copy);
            added.Add(copy);
        }

        return (added, alreadyThere);
    }
}
