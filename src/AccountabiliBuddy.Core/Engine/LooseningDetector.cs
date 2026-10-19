using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Finds the changes between two configurations that make focus easier to escape ("looser"), described in plain
/// words. Everything not listed here is stricter or neutral and may take effect at once.
/// <list type="bullet">
/// <item>Schedule: a weekly or one-off block no longer fully covered by a block with the same profile; time off
/// not fully covered by existing time off.</item>
/// <item>Profile: a Block rule removed, an Allow rule added, a longer grace period, "ask about unlisted sites"
/// turned off, an unlock option added or its wait shortened, or the profile deleted while the schedule uses it.</item>
/// <item>Settings: a shorter end-early wait or reason, later escalation, relock at midnight turned off, or a
/// shorter cooling-off for looser changes.</item>
/// </list>
/// Rules are compared by what they match (target, kind, pattern), not by id, so editing a note is neutral.
/// </summary>
public static class LooseningDetector
{
    private const int MinutesPerDay = 24 * 60;
    private const int MinutesPerWeek = 7 * MinutesPerDay;

    public static IReadOnlyList<string> Find(ProfileDocument from, ProfileDocument to)
    {
        var changes = new List<string>(Schedule(from, to));

        foreach (FocusProfile before in from.Profiles)
        {
            FocusProfile? after = to.Profiles.FirstOrDefault(profile => profile.Id == before.Id);
            changes.AddRange(Profile(from, before, after));
        }

        changes.AddRange(Settings(from, to));
        return changes;
    }

    public static IReadOnlyList<string> Schedule(ProfileDocument from, ProfileDocument to)
    {
        var changes = new List<string>();

        foreach (ScheduleBlock block in from.Schedule.Blocks)
        {
            if (!to.Schedule.Blocks.Any(candidate => Covers(candidate, block)))
            {
                changes.Add($"Removed or shortened block {Describe(block, from)}");
            }
        }

        foreach (OneOffBlock block in from.Schedule.OneOffBlocks)
        {
            LocalWindow window = block.GetWindow();
            if (!to.Schedule.OneOffBlocks.Any(candidate =>
                    candidate.ProfileId == block.ProfileId && candidate.GetWindow().Covers(window)))
            {
                changes.Add($"Removed or shortened one-off block {Describe(block, from)}");
            }
        }

        foreach (TimeOff timeOff in to.Schedule.TimeOff.OrderBy(off => off.GetWindow().Start))
        {
            LocalWindow window = timeOff.GetWindow();
            if (!from.Schedule.TimeOff.Any(existing => existing.GetWindow().Covers(window)))
            {
                changes.Add($"Time off {Describe(timeOff)}");
            }
        }

        return changes;
    }

    /// <param name="after">The profile after the change; null when it was deleted.</param>
    public static IReadOnlyList<string> Profile(ProfileDocument from, FocusProfile before, FocusProfile? after)
    {
        if (after is null)
        {
            return from.Schedule.Blocks.Any(block => block.ProfileId == before.Id)
                   || from.Schedule.OneOffBlocks.Any(block => block.ProfileId == before.Id)
                ? new[] { $"Deleted profile '{before.Name}' (the schedule uses it)" }
                : Array.Empty<string>();
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

    /// <summary>True when every minute of <paramref name="inner"/> is also blocked by <paramref name="outer"/> with the same profile.</summary>
    private static bool Covers(ScheduleBlock outer, ScheduleBlock inner)
    {
        if (outer.ProfileId != inner.ProfileId)
        {
            return false;
        }

        // Positions in the week, so blocks that run past midnight (or from Saturday into Sunday) compare correctly.
        int offset = ((StartInWeek(inner) - StartInWeek(outer)) % MinutesPerWeek + MinutesPerWeek) % MinutesPerWeek;
        return offset + LengthInMinutes(inner) <= LengthInMinutes(outer);
    }

    private static int StartInWeek(ScheduleBlock block)
    {
        return (int)block.Day * MinutesPerDay + block.Start.Hour * 60 + block.Start.Minute;
    }

    private static int LengthInMinutes(ScheduleBlock block)
    {
        int length = block.End.Hour * 60 + block.End.Minute - (block.Start.Hour * 60 + block.Start.Minute);
        return length > 0 ? length : length + MinutesPerDay;
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

    private static string Describe(ScheduleBlock block, ProfileDocument document)
    {
        string profile = document.Profiles.FirstOrDefault(candidate => candidate.Id == block.ProfileId)?.Name
                         ?? "deleted profile";
        return $"{block.Day.ToString()[..3]} {block.Start:HH\\:mm}–{block.End:HH\\:mm} ({profile})";
    }

    private static string Describe(OneOffBlock block, ProfileDocument document)
    {
        string profile = document.Profiles.FirstOrDefault(candidate => candidate.Id == block.ProfileId)?.Name
                         ?? "deleted profile";
        return $"{block.Date:ddd d MMM} {block.Start:HH\\:mm}–{block.End:HH\\:mm} ({profile})";
    }

    private static string Describe(TimeOff timeOff)
    {
        return timeOff.IsAllDay
            ? $"{timeOff.Date:ddd d MMM}, all day"
            : $"{timeOff.Date:ddd d MMM} {timeOff.Start:HH\\:mm}–{timeOff.End:HH\\:mm}";
    }

    private static string Format(TimeSpan duration)
    {
        return duration < TimeSpan.FromMinutes(2)
            ? $"{duration.TotalSeconds:0} s"
            : $"{duration.TotalMinutes:0} min";
    }
}
