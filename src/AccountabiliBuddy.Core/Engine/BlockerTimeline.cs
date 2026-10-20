using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Import;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Places blockers' blocks on real dates for the timeline's Blockers row. Read only: it describes what the blockers
/// enforce, it never changes them.
/// </summary>
public static class BlockerTimeline
{
    /// <summary>
    /// When each of Cold Turkey's switched-on blocks blocks during <paramref name="range"/>: the stretches of its weekly
    /// schedule that touch the range, with their real times (the caller clips them for drawing), or the whole range for
    /// a block with no schedule (<see cref="BlockerWindow.AlwaysOn"/>). Nothing while Cold Turkey is paused. A schedule
    /// split at Saturday midnight (or into touching ranges) shows as one stretch.
    /// </summary>
    /// <returns>Ordered by start, then by name.</returns>
    public static IReadOnlyList<BlockerWindow> ColdTurkey(ColdTurkeyBlocks blocks, LocalWindow range)
    {
        var windows = new List<BlockerWindow>();
        if (blocks.IsPaused || range.End <= range.Start)
        {
            return windows;
        }

        foreach (ColdTurkeyBlockState block in blocks.Blocks.Where(block => block.IsSwitchedOn))
        {
            if (!block.IsScheduled)
            {
                windows.Add(new BlockerWindow(block.Name, range, AlwaysOn: true));
                continue;
            }

            windows.AddRange(Merge(ScheduleAround(block.Schedule, range))
                .Where(window => DayTimeline.Clip(window, range) is not null)
                .Select(window => new BlockerWindow(block.Name, window)));
        }

        return windows
            .OrderBy(window => window.Window.Start)
            .ThenBy(window => window.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The weekly ranges on every week around the range (from the week before, for spans that wrap).</summary>
    private static IEnumerable<LocalWindow> ScheduleAround(IReadOnlyList<WeekRange> schedule, LocalWindow range)
    {
        DateTime firstSunday = range.Start.Date.AddDays(-(int)range.Start.DayOfWeek).AddDays(-7);
        for (DateTime sunday = firstSunday; sunday <= range.End.AddDays(7); sunday = sunday.AddDays(7))
        {
            foreach (WeekRange weekRange in schedule)
            {
                yield return new LocalWindow(sunday.AddMinutes(weekRange.Start), sunday.AddMinutes(weekRange.End));
            }
        }
    }

    /// <summary>Joins stretches that touch or overlap.</summary>
    private static List<LocalWindow> Merge(IEnumerable<LocalWindow> stretches)
    {
        var merged = new List<LocalWindow>();
        foreach (LocalWindow stretch in stretches.OrderBy(stretch => stretch.Start))
        {
            if (merged.Count > 0 && stretch.Start <= merged[^1].End)
            {
                LocalWindow last = merged[^1];
                merged[^1] = last with { End = stretch.End > last.End ? stretch.End : last.End };
            }
            else
            {
                merged.Add(stretch);
            }
        }

        return merged;
    }
}
