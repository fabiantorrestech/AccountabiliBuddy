namespace AccountabiliBuddy.Core.Domain;

/// <summary>Weekly blocked time, minus <see cref="OffDays"/>: dates planned ahead on which no block starts (schema v2).</summary>
public sealed record WeeklySchedule(IReadOnlyList<ScheduleBlock> Blocks)
{
    public static WeeklySchedule Empty { get; } = new(Array.Empty<ScheduleBlock>());

    public IReadOnlyList<DateOnly> OffDays { get; init; } = Array.Empty<DateOnly>();

    /// <summary>
    /// Returns the block active at <paramref name="localTime"/>. When blocks overlap,
    /// the one ending latest wins so the session is not cut short.
    /// </summary>
    public ActiveScheduleBlock? FindActive(DateTime localTime)
    {
        ActiveScheduleBlock? best = null;

        foreach (ScheduleBlock block in Blocks)
        {
            if (!block.TryGetOccurrence(localTime, out DateTime start, out DateTime end)
                || OffDays.Contains(DateOnly.FromDateTime(start)))
            {
                continue;
            }

            if (best is null || end > best.LocalEnd)
            {
                best = new ActiveScheduleBlock(block, start, end);
            }
        }

        return best;
    }
}
