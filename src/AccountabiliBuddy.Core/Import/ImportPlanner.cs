using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Import;

/// <summary>
/// Turns imported block lists into new profiles and weekly blocks.
/// <list type="bullet">
/// <item>Each list becomes a profile that only blocks what's listed: it gets visible "allow everything else" rules
/// and doesn't ask about unlisted sites, like the blocker it came from.</item>
/// <item>Its schedule becomes weekly blocks. Where the schedules of several lists overlap, that time gets one
/// combined profile ("A + B") with all their rules, because only one profile runs at a time.</item>
/// <item>Nothing existing is changed: names that clash get the source added, and imported times that overlap
/// existing weekly blocks are left out (and listed in the notes).</item>
/// </list>
/// </summary>
public static class ImportPlanner
{
    public const string AllowOtherAppsNote = "Allows every app not listed (imported block list)";
    public const string AllowOtherSitesNote = "Allows every site not listed (imported block list)";

    public static ImportPlan Plan(IReadOnlyList<ImportedBlockList> lists, ProfileDocument existing, TimeSpan gracePeriod)
    {
        var notes = new List<string>();
        var usable = new List<ImportedBlockList>();
        foreach (ImportedBlockList list in lists)
        {
            notes.AddRange(list.Notes);
            if (list.IsEmpty)
            {
                notes.Add($"{list.Name}: nothing to import (no sites or apps).");
            }
            else
            {
                usable.Add(list);
            }
        }

        var usedNames = new HashSet<string>(existing.Profiles.Select(profile => profile.Name), StringComparer.OrdinalIgnoreCase);
        var profiles = new List<PlannedProfile>();
        var profileOfList = new List<PlannedProfile>();
        foreach (ImportedBlockList list in usable)
        {
            PlannedProfile planned = CreateProfile(new[] { list }, usedNames, gracePeriod);
            profiles.Add(planned);
            profileOfList.Add(planned);
        }

        IReadOnlyList<WeekRange> occupied = existing.Schedule.Blocks.SelectMany(RangesOf).ToList();
        var combined = new Dictionary<string, PlannedProfile>(StringComparer.Ordinal);
        var blocks = new List<ScheduleBlock>();
        foreach (Segment segment in Sweep(usable, occupied, notes))
        {
            PlannedProfile owner;
            if (segment.Lists.Length == 1)
            {
                owner = profileOfList[segment.Lists[0]];
            }
            else
            {
                string key = string.Join(',', segment.Lists);
                if (!combined.TryGetValue(key, out owner!))
                {
                    owner = CreateProfile(segment.Lists.Select(index => usable[index]).ToList(), usedNames, gracePeriod);
                    combined[key] = owner;
                    profiles.Add(owner);
                }
            }

            blocks.AddRange(ToScheduleBlocks(segment.Start, segment.End, owner.Profile.Id));
        }

        return new ImportPlan(profiles, blocks, notes);
    }

    public static string SourceLabel(ImportSource source)
    {
        return source switch
        {
            ImportSource.ColdTurkey => "Cold Turkey",
            ImportSource.AppBlock => "AppBlock",
            ImportSource.BlockSite => "BlockSite",
            ImportSource.LeechBlock => "LeechBlock",
            _ => "pasted list",
        };
    }

    private static PlannedProfile CreateProfile(IReadOnlyList<ImportedBlockList> members, HashSet<string> usedNames, TimeSpan gracePeriod)
    {
        var rules = new List<Rule>();
        var skipped = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ImportedBlockList member in members)
        {
            string note = $"Imported from {SourceLabel(member.Source)}: {member.Name}";
            BlockListParseResult blocked = BlockListParser.Parse(string.Join('\n', member.BlockEntries), RuleVerdict.Block, note);
            BlockListParseResult allowed = BlockListParser.Parse(string.Join('\n', member.AllowEntries), RuleVerdict.Allow, note);
            foreach (Rule rule in blocked.Rules.Concat(allowed.Rules))
            {
                if (seen.Add($"{rule.Target}|{rule.Kind}|{rule.Pattern}|{rule.Verdict}"))
                {
                    rules.Add(rule);
                }
            }

            skipped.AddRange(blocked.SkippedLines.Concat(allowed.SkippedLines));
        }

        rules.Add(new Rule(Guid.NewGuid(), MatchTarget.ProcessName, MatchKind.Regex, ".", RuleVerdict.Allow, AllowOtherAppsNote));
        rules.Add(new Rule(Guid.NewGuid(), MatchTarget.Url, MatchKind.Regex, ".", RuleVerdict.Allow, AllowOtherSitesNote));

        string name = UniqueName(string.Join(" + ", members.Select(member => member.Name)), members[0].Source, usedNames);
        var profile = new FocusProfile(Guid.NewGuid(), name, rules, gracePeriod)
        {
            UnlockOptions = UnlockOptionsFor(members),
            AskAboutUnlistedSites = false,
        };
        return new PlannedProfile(profile, members.Select(member => member.Name).ToList(), skipped);
    }

    /// <summary>
    /// A list that says nothing about breaks gets the app's default unlock option. A combined profile gets an
    /// unlock option only if every list in it has one, with the longest wait.
    /// </summary>
    private static IReadOnlyList<UnlockOption> UnlockOptionsFor(IReadOnlyList<ImportedBlockList> members)
    {
        List<IReadOnlyList<UnlockOption>> resolved = members
            .Select(member => member.UnlockOptions ?? new[] { UnlockOption.CreateOffDay() })
            .ToList();
        if (resolved.Count == 1)
        {
            return resolved[0].Select(option => option with { Id = Guid.NewGuid() }).ToList();
        }

        if (resolved.Any(options => options.Count == 0))
        {
            return Array.Empty<UnlockOption>();
        }

        UnlockOption longest = resolved.SelectMany(options => options).MaxBy(option => option.Wait)!;
        return new[] { longest with { Id = Guid.NewGuid() } };
    }

    private static string UniqueName(string name, ImportSource source, HashSet<string> usedNames)
    {
        string candidate = usedNames.Contains(name) ? $"{name} ({SourceLabel(source)})" : name;
        string unique = candidate;
        for (int number = 2; usedNames.Contains(unique); number++)
        {
            unique = $"{candidate} {number}";
        }

        usedNames.Add(unique);
        return unique;
    }

    /// <summary>
    /// Splits the week at every start and end, keeps the parts some list covers and no existing block does, and
    /// joins neighbouring parts covered by the same lists (also across Saturday midnight).
    /// </summary>
    private static List<Segment> Sweep(IReadOnlyList<ImportedBlockList> lists, IReadOnlyList<WeekRange> occupied, List<string> notes)
    {
        var points = new SortedSet<int> { 0, WeekRange.MinutesPerWeek };
        foreach (WeekRange range in lists.SelectMany(list => list.Schedule).Concat(occupied))
        {
            points.Add(range.Start);
            points.Add(range.End);
        }

        int[] boundaries = points.ToArray();
        var segments = new List<Segment>();
        var clashes = new List<Segment>();
        for (int i = 0; i + 1 < boundaries.Length; i++)
        {
            int start = boundaries[i];
            int end = boundaries[i + 1];
            int[] covering = Enumerable.Range(0, lists.Count)
                .Where(index => lists[index].Schedule.Any(range => range.Start <= start && end <= range.End))
                .ToArray();
            if (covering.Length == 0)
            {
                continue;
            }

            bool alreadyScheduled = occupied.Any(range => range.Start <= start && end <= range.End);
            AddJoined(alreadyScheduled ? clashes : segments, new Segment(start, end, covering));
        }

        if (segments.Count > 1 && segments[^1].End == WeekRange.MinutesPerWeek && segments[0].Start == 0
            && segments[^1].Lists.SequenceEqual(segments[0].Lists))
        {
            segments[^1] = segments[^1] with { End = WeekRange.MinutesPerWeek + segments[0].End };
            segments.RemoveAt(0);
        }

        foreach (Segment clash in clashes)
        {
            notes.Add($"{WeekRange.Describe(clash.Start, clash.End)}: overlaps your existing schedule, so it was left out.");
        }

        return segments;
    }

    private static void AddJoined(List<Segment> segments, Segment next)
    {
        if (segments.Count > 0 && segments[^1].End == next.Start && segments[^1].Lists.SequenceEqual(next.Lists))
        {
            segments[^1] = segments[^1] with { End = next.End };
        }
        else
        {
            segments.Add(next);
        }
    }

    /// <summary>One block if it lasts a day or less (it may cross midnight), otherwise one per day.</summary>
    private static IEnumerable<ScheduleBlock> ToScheduleBlocks(int start, int end, Guid profileId)
    {
        if (end - start <= WeekRange.MinutesPerDay)
        {
            yield return Block(start, end, profileId);
            yield break;
        }

        for (int from = start; from < end;)
        {
            int nextMidnight = (from / WeekRange.MinutesPerDay + 1) * WeekRange.MinutesPerDay;
            int to = Math.Min(end, nextMidnight);
            yield return Block(from, to, profileId);
            from = to;
        }
    }

    private static ScheduleBlock Block(int start, int end, Guid profileId)
    {
        var day = (DayOfWeek)(start / WeekRange.MinutesPerDay % 7);
        return new ScheduleBlock(Guid.NewGuid(), day, TimeOf(start), TimeOf(end), profileId);
    }

    private static TimeOnly TimeOf(int weekMinute)
    {
        return TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(weekMinute % WeekRange.MinutesPerDay));
    }

    private static IEnumerable<WeekRange> RangesOf(ScheduleBlock block)
    {
        int start = (int)block.Day * WeekRange.MinutesPerDay + (int)block.Start.ToTimeSpan().TotalMinutes;
        int end = (int)block.Day * WeekRange.MinutesPerDay + (int)block.End.ToTimeSpan().TotalMinutes;
        if (block.CrossesMidnight)
        {
            end += WeekRange.MinutesPerDay;
        }

        return WeekRange.FromWeekMinutes(start, end);
    }

    /// <param name="Lists">Indexes of the lists covering this part of the week, ascending.</param>
    private readonly record struct Segment(int Start, int End, int[] Lists);
}
