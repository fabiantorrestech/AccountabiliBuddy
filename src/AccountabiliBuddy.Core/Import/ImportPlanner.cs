using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Import;

/// <summary>
/// Turns imported block lists into new profiles.
/// <list type="bullet">
/// <item>Each list becomes a profile that only blocks what's listed: it gets visible "allow everything else" rules
/// and doesn't ask about unlisted sites, like the blocker it came from.</item>
/// <item>Its schedule stays with the blocker it came from, which enforces it (ADR 18).</item>
/// <item>Nothing existing is changed: names that clash get the source added.</item>
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
        var profiles = usable.Select(list => CreateProfile(new[] { list }, usedNames, gracePeriod)).ToList();
        return new ImportPlan(profiles, notes);
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
}
