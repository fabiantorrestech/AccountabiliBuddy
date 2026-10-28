using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Deep work per day (redesign phase 4): this PC's counted sessions (the same rule as the day bar and the sync files:
/// no omitted sessions, no old scheduled blocks, time stepped away left out) together with the other devices' synced
/// sessions; time two devices both had in focus counts once.
/// </summary>
public static class DeepWork
{
    /// <param name="records">This PC's sessions, newest first (<see cref="SessionHistoryBuilder.Build"/>).</param>
    /// <param name="others">The other devices' sessions (ADR 22); one without an end runs until <paramref name="now"/>.</param>
    /// <returns>One entry per date from <paramref name="from"/> to <paramref name="to"/>, empty days included.</returns>
    public static IReadOnlyList<DeepWorkDay> Days(
        IReadOnlyList<SessionRecord> records,
        IEnumerable<SyncSession> others,
        Func<DateTimeOffset, bool> isOmitted,
        bool sessionRunning,
        DateOnly from,
        DateOnly to,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        return DaysIn(Windows(records, others, isOmitted, sessionRunning, now, zone), from, to);
    }

    /// <summary>
    /// The stretches of counted focus across the user's devices as local wall-clock windows, earliest first, time two devices
    /// both had in focus counted once (the Stats page draws its day strip and intensity grid from these).
    /// </summary>
    /// <param name="records">This PC's sessions, newest first (<see cref="SessionHistoryBuilder.Build"/>).</param>
    /// <param name="others">The other devices' sessions (ADR 22); one without an end runs until <paramref name="now"/>.</param>
    public static IReadOnlyList<LocalWindow> Windows(
        IReadOnlyList<SessionRecord> records,
        IEnumerable<SyncSession> others,
        Func<DateTimeOffset, bool> isOmitted,
        bool sessionRunning,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        var windows = new List<LocalWindow>();
        foreach ((SessionRecord record, DateTimeOffset end, bool _) in FocusDayBuilder.CountedRecords(records, isOmitted, sessionRunning, now))
        {
            var session = new LocalWindow(FocusDayBuilder.Local(record.StartedAt, zone), FocusDayBuilder.Local(end, zone));
            IEnumerable<LocalWindow> away = (record.Away ?? [])
                .Where(stretch => stretch.From < end)
                .Select(stretch => new LocalWindow(FocusDayBuilder.Local(stretch.From, zone), FocusDayBuilder.Local(stretch.To < end ? stretch.To : end, zone)));
            windows.AddRange(DayTimeline.Subtract([session], away));
        }

        foreach (SyncSession other in others)
        {
            DateTimeOffset end = other.End ?? now;
            if (end > other.Start)
            {
                windows.Add(new LocalWindow(FocusDayBuilder.Local(other.Start, zone), FocusDayBuilder.Local(end, zone)));
            }
        }

        return DayTimeline.Union(windows);
    }

    /// <summary>Each date from <paramref name="from"/> to <paramref name="to"/> with the part of <paramref name="focused"/> inside it.</summary>
    public static IReadOnlyList<DeepWorkDay> DaysIn(IReadOnlyList<LocalWindow> focused, DateOnly from, DateOnly to)
    {
        var days = new List<DeepWorkDay>();
        for (DateOnly date = from; date <= to; date = date.AddDays(1))
        {
            DateTime start = date.ToDateTime(TimeOnly.MinValue);
            var day = new LocalWindow(start, start.AddDays(1));
            List<TimeSpan> pieces = focused
                .Select(window => DayTimeline.Clip(window, day))
                .OfType<LocalWindow>()
                .Select(window => window.End - window.Start)
                .ToList();
            days.Add(new DeepWorkDay(date, pieces.Aggregate(TimeSpan.Zero, (sum, piece) => sum + piece), pieces.DefaultIfEmpty(TimeSpan.Zero).Max()));
        }

        return days;
    }
}
