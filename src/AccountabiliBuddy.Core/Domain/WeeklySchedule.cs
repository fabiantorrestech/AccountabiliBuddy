namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// Weekly blocked time, plus <see cref="OneOffBlocks"/> on single dates, minus <see cref="TimeOff"/> (schema v4;
/// time off replaced v2's off days, which became all-day time off).
/// </summary>
public sealed record WeeklySchedule(IReadOnlyList<ScheduleBlock> Blocks)
{
    public static WeeklySchedule Empty { get; } = new(Array.Empty<ScheduleBlock>());

    public IReadOnlyList<TimeOff> TimeOff { get; init; } = Array.Empty<TimeOff>();

    public IReadOnlyList<OneOffBlock> OneOffBlocks { get; init; } = Array.Empty<OneOffBlock>();

    /// <summary>
    /// Returns the block active at <paramref name="localTime"/>, trimmed to the part not covered by time off:
    /// it starts after the last time off that ended within the occurrence and ends when the next time off starts.
    /// Nothing is active during time off. When blocks overlap, the one ending latest wins so the session is not
    /// cut short.
    /// </summary>
    public ActiveScheduleBlock? FindActive(DateTime localTime)
    {
        List<LocalWindow> timeOff = TimeOff.Select(off => off.GetWindow()).ToList();
        if (timeOff.Any(window => window.Contains(localTime)))
        {
            return null;
        }

        ActiveScheduleBlock? best = null;
        foreach ((ScheduleBlock block, LocalWindow occurrence) in OccurrencesAt(localTime))
        {
            DateTime start = timeOff
                .Where(window => window.End > occurrence.Start && window.End <= localTime)
                .Select(window => window.End)
                .Append(occurrence.Start)
                .Max();
            DateTime end = timeOff
                .Where(window => window.Start > localTime && window.Start < occurrence.End)
                .Select(window => window.Start)
                .Append(occurrence.End)
                .Min();

            if (best is null || end > best.LocalEnd)
            {
                best = new ActiveScheduleBlock(block, start, end);
            }
        }

        return best;
    }

    private IEnumerable<(ScheduleBlock Block, LocalWindow Occurrence)> OccurrencesAt(DateTime localTime)
    {
        foreach (ScheduleBlock block in Blocks)
        {
            if (block.TryGetOccurrence(localTime, out DateTime start, out DateTime end))
            {
                yield return (block, new LocalWindow(start, end));
            }
        }

        foreach (OneOffBlock oneOff in OneOffBlocks)
        {
            LocalWindow window = oneOff.GetWindow();
            if (window.Contains(localTime))
            {
                yield return (oneOff.ToScheduleBlock(), window);
            }
        }
    }
}
