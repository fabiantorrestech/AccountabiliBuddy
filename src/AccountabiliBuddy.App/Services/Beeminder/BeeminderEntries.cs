using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>
/// The pure rules for the goal entries Beeminder's datapoints become (ADR 84): which goals are linked to read, one entry per goal and
/// link and day with an id that never changes, updated in place, and removed when the link or its direction goes. Hand entries are never
/// touched (a hand value wins in <see cref="GoalCounting"/>). ADR 79 (GS3a): the entries travel to the user's other devices like every
/// goal entry, so a removal is a tombstone (kept for good, it reaches every device and a stale file never brings the day back) and only
/// a PC with a Beeminder token changes or removes them (<see cref="GoalDerivedWriter.MayChange"/>): a PC without one keeps the values
/// another PC read. A read brings a removed day back when Beeminder has it again (a goal linked again).
/// </summary>
public static partial class BeeminderEntries
{
    /// <summary>How far back the first read goes.</summary>
    public static readonly TimeSpan FirstRead = TimeSpan.FromDays(90);

    /// <summary>A slug Beeminder allows in an address; anything else is never put in a URL.</summary>
    public static bool IsValidSlug(string slug) => SlugPattern().IsMatch(slug);

    /// <summary>
    /// The goals that read Beeminder: a live goal's source of that type, direction Read, switched on and with a goal picked. Deep work
    /// never has one (<see cref="GoalSources.CountedFor"/>: it counts focus only).
    /// </summary>
    public static IReadOnlyList<BeeminderLink> LinksOf(IEnumerable<Goal> liveGoals)
    {
        var links = new List<BeeminderLink>();
        foreach (Goal goal in liveGoals)
        {
            foreach (GoalSourceRule rule in GoalSources.CountedFor(goal))
            {
                if (rule.Type == GoalSourceType.Beeminder && rule.Automatic && rule.BeeminderDirection == GoalBeeminderDirection.Read
                    && !string.IsNullOrWhiteSpace(rule.BeeminderSlug))
                {
                    links.Add(new BeeminderLink(goal.Id, rule.BeeminderSlug.Trim()));
                }
            }
        }

        return links.Distinct().ToList();
    }

    /// <summary>The entry's id for a goal's link and day: the same every time, on every read.</summary>
    public static Guid IdFor(Guid goalId, string key, DateOnly day)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"beeminder|{goalId:D}|{key.ToLowerInvariant()}|{day:yyyy-MM-dd}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>The newest day this link already has an entry for; null with none.</summary>
    public static DateOnly? NewestDay(GoalLists lists, BeeminderLink link)
    {
        DateOnly? newest = null;
        foreach (GoalEntry entry in lists.Entries)
        {
            if (entry.Source == GoalEntrySource.Beeminder && entry.GoalId == link.GoalId && entry.Id == IdFor(link.GoalId, link.Key, entry.Day)
                && (newest is null || entry.Day > newest))
            {
                newest = entry.Day;
            }
        }

        return newest;
    }

    /// <summary>
    /// On a PC with a Beeminder token (<paramref name="hasToken"/>): removes (a tombstone) every live Beeminder entry of a live goal that no
    /// live Read link owns (unlinked, direction changed, source switched off). A deleted goal's entries stay, hidden, and come back with it
    /// (ADR 79). Without a token nothing is removed: the values may be another PC's reading (GS3a, plan check 1.10).
    /// </summary>
    public static GoalLists Prune(GoalLists lists, IReadOnlyCollection<BeeminderLink> active, bool hasToken, DateTimeOffset now)
    {
        if (!hasToken)
        {
            return lists;
        }

        var liveGoals = lists.Goals.Where(goal => goal.IsLive).Select(goal => goal.Id).ToHashSet();
        bool changed = false;
        List<GoalEntry> entries = lists.Entries.Select(entry =>
        {
            if (entry.Source != GoalEntrySource.Beeminder || !entry.IsLive || !liveGoals.Contains(entry.GoalId)
                || active.Any(link => link.GoalId == entry.GoalId && IdFor(link.GoalId, link.Key, entry.Day) == entry.Id))
            {
                return entry;
            }

            changed = true;
            return Removed(entry, now);
        }).ToList();
        return changed ? lists with { Entries = entries } : lists;
    }

    /// <summary>
    /// Puts a read's day values in: a day's entry is made, or updated in place when its value changed (its edit time then moves), or
    /// brought back when it was removed (ADR 79: a removal is a tombstone); a day from <paramref name="from"/> on that the read no longer
    /// has loses its entry (a tombstone: the datapoint was deleted on Beeminder). Returns the same lists when nothing changed.
    /// </summary>
    public static GoalLists Apply(GoalLists lists, BeeminderLink link, DateOnly from, IReadOnlyDictionary<DateOnly, double> days, DateTimeOffset now)
    {
        var entries = lists.Entries.ToList();
        bool changed = false;
        foreach ((DateOnly day, double value) in days)
        {
            Guid id = IdFor(link.GoalId, link.Key, day);
            int index = entries.FindIndex(entry => entry.Id == id);
            if (index < 0)
            {
                entries.Add(new GoalEntry(id, link.GoalId, day, null, value, GoalEntrySource.Beeminder, now));
                changed = true;
            }
            else if (!entries[index].IsLive || entries[index].Value != value)
            {
                entries[index] = entries[index] with { Value = value, EditedAt = Next(now, entries[index]), DeletedAt = null };
                changed = true;
            }
        }

        for (int index = 0; index < entries.Count; index++)
        {
            GoalEntry entry = entries[index];
            if (entry.IsLive && entry.Source == GoalEntrySource.Beeminder && entry.GoalId == link.GoalId && entry.Day >= from
                && !days.ContainsKey(entry.Day) && entry.Id == IdFor(link.GoalId, link.Key, entry.Day))
            {
                entries[index] = Removed(entry, now);
                changed = true;
            }
        }

        return changed ? lists with { Entries = entries } : lists;
    }

    // A tombstone stamped after the entry's own stamp (another PC's clock may run ahead), so it wins the merge everywhere.
    private static GoalEntry Removed(GoalEntry entry, DateTimeOffset now)
    {
        DateTimeOffset at = Next(now, entry);
        return entry with { DeletedAt = at, EditedAt = at };
    }

    private static DateTimeOffset Next(DateTimeOffset now, GoalEntry entry)
    {
        return entry.Stamp >= now ? entry.Stamp.AddTicks(1) : now;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+\\z")]
    private static partial Regex SlugPattern();
}
