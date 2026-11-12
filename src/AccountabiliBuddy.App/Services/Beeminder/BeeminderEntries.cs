using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>
/// The pure rules for the goal entries Beeminder's datapoints become (ADR 84): which goals are linked to read, one entry per goal and
/// link and day with an id that never changes, updated in place, and removed when the link, its direction or the token goes. Hand
/// entries are never touched (a hand value wins in <see cref="GoalCounting"/>).
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

    /// <summary>Drops every Beeminder entry that no live Read link owns (unlinked, direction changed, goal deleted, token removed).</summary>
    public static GoalLists Prune(GoalLists lists, IReadOnlyCollection<BeeminderLink> active)
    {
        List<GoalEntry> kept = lists.Entries
            .Where(entry => entry.Source != GoalEntrySource.Beeminder
                || active.Any(link => link.GoalId == entry.GoalId && IdFor(link.GoalId, link.Key, entry.Day) == entry.Id))
            .ToList();
        return kept.Count == lists.Entries.Count ? lists : lists with { Entries = kept };
    }

    /// <summary>
    /// Puts a read's day values in: a day's entry is made, or updated in place when its value changed (its edit time then moves); a
    /// day from <paramref name="from"/> on that the read no longer has loses its entry (the datapoint was deleted on Beeminder). A
    /// removed (tombstoned) entry stays removed. Returns the same lists when nothing changed.
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
            else if (entries[index].IsLive && entries[index].Value != value)
            {
                entries[index] = entries[index] with { Value = value, EditedAt = now };
                changed = true;
            }
        }

        int removed = entries.RemoveAll(entry => entry.Source == GoalEntrySource.Beeminder && entry.GoalId == link.GoalId && entry.Day >= from
            && !days.ContainsKey(entry.Day) && entry.Id == IdFor(link.GoalId, link.Key, entry.Day));
        return changed || removed > 0 ? lists with { Entries = entries } : lists;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+\\z")]
    private static partial Regex SlugPattern();
}
