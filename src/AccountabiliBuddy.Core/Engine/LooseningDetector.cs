using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Finds the changes between two configurations that make focus easier to escape ("looser"), described in plain
/// words. Everything not listed here is stricter or neutral and may take effect at once.
/// <list type="bullet">
/// <item>Profile: a Block rule removed, an Allow rule added, a longer grace period, "ask about unlisted sites"
/// turned off, or an unlock option added or its wait shortened.</item>
/// <item>Settings: a shorter end-early wait or reason, later escalation, relock at midnight turned off, or a
/// shorter cooling-off for looser changes.</item>
/// </list>
/// Rules are compared by what they match (target, kind, pattern), not by id, so editing a note is neutral.
/// The day plan is never looser: it only says when the buddy watches (ADR 18).
/// </summary>
public static class LooseningDetector
{
    public static IReadOnlyList<string> Find(ProfileDocument from, ProfileDocument to)
    {
        var changes = new List<string>();

        foreach (FocusProfile before in from.Profiles)
        {
            FocusProfile? after = to.Profiles.FirstOrDefault(profile => profile.Id == before.Id);
            changes.AddRange(Profile(from, before, after));
        }

        changes.AddRange(Settings(from, to));
        return changes;
    }

    /// <param name="after">The profile after the change; null when it was deleted (never looser: nothing runs it on a schedule).</param>
    public static IReadOnlyList<string> Profile(ProfileDocument from, FocusProfile before, FocusProfile? after)
    {
        if (after is null)
        {
            return Array.Empty<string>();
        }

        var changes = new List<string>();
        string name = before.Name;

        HashSet<string> blocksAfter = Keys(after.Rules, RuleVerdict.Block);
        foreach (Rule rule in before.Rules.Where(rule => rule.Verdict == RuleVerdict.Block))
        {
            if (!blocksAfter.Contains(Key(rule)))
            {
                changes.Add($"{name}: removed block rule {Describe(rule)}");
            }
        }

        HashSet<string> allowsBefore = Keys(before.Rules, RuleVerdict.Allow);
        foreach (Rule rule in after.Rules.Where(rule => rule.Verdict == RuleVerdict.Allow))
        {
            if (!allowsBefore.Contains(Key(rule)))
            {
                changes.Add($"{name}: added allow rule {Describe(rule)}");
            }
        }

        if (after.UnknownGracePeriod > before.UnknownGracePeriod)
        {
            changes.Add($"{name}: asks about unknown apps after {Format(before.UnknownGracePeriod)} → {Format(after.UnknownGracePeriod)}");
        }

        if (before.AskAboutUnlistedSites && !after.AskAboutUnlistedSites)
        {
            changes.Add($"{name}: stopped asking about sites with no rule");
        }

        foreach (UnlockOption option in after.UnlockOptions)
        {
            UnlockOption? earlier = before.UnlockOptions.FirstOrDefault(candidate => candidate.Id == option.Id);
            if (earlier is null)
            {
                changes.Add($"{name}: added unlock option '{option.Name}' (wait {Format(option.Wait)})");
            }
            else if (option.Wait < earlier.Wait)
            {
                changes.Add($"{name}: unlock '{option.Name}' wait {Format(earlier.Wait)} → {Format(option.Wait)}");
            }
        }

        return changes;
    }

    public static IReadOnlyList<string> Settings(ProfileDocument from, ProfileDocument to)
    {
        var changes = new List<string>();

        if (to.Bypass.WaitDuration < from.Bypass.WaitDuration)
        {
            changes.Add($"End-early wait {Format(from.Bypass.WaitDuration)} → {Format(to.Bypass.WaitDuration)}");
        }

        if (to.Bypass.MinReasonLength < from.Bypass.MinReasonLength)
        {
            changes.Add($"End-early reason {from.Bypass.MinReasonLength} → {to.Bypass.MinReasonLength} characters");
        }

        AddIfLater(changes, "Notification", from.Escalation.NudgeAfter, to.Escalation.NudgeAfter);
        AddIfLater(changes, "Screen overlay", from.Escalation.OverlayAfter, to.Escalation.OverlayAfter);
        AddIfLater(changes, "Minimize / block", from.Escalation.EnforceAfter, to.Escalation.EnforceAfter);

        if (from.Locks.RelockAtMidnight && !to.Locks.RelockAtMidnight)
        {
            changes.Add("Relock at midnight turned off");
        }

        if (to.Locks.LooseningDelay < from.Locks.LooseningDelay)
        {
            changes.Add($"Looser changes wait {Format(from.Locks.LooseningDelay)} → {Format(to.Locks.LooseningDelay)}");
        }

        return changes;
    }

    private static void AddIfLater(List<string> changes, string label, TimeSpan before, TimeSpan after)
    {
        if (after > before)
        {
            changes.Add($"{label} after {Format(before)} → {Format(after)}");
        }
    }

    private static HashSet<string> Keys(IEnumerable<Rule> rules, RuleVerdict verdict)
    {
        return new HashSet<string>(
            rules.Where(rule => rule.Verdict == verdict).Select(Key),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string Key(Rule rule)
    {
        return $"{rule.Target}|{rule.Kind}|{rule.Pattern.Trim()}";
    }

    private static string Describe(Rule rule)
    {
        return $"{rule.Target} '{rule.Pattern.Trim()}'";
    }

    private static string Format(TimeSpan duration)
    {
        return duration < TimeSpan.FromMinutes(2)
            ? $"{duration.TotalSeconds:0} s"
            : $"{duration.TotalMinutes:0} min";
    }
}
