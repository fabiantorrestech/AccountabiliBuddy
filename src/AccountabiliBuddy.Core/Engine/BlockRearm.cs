using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// When blocks go off or come back on (ADR 37), from the day plan's block rules for one device: for a day, its
/// re-enable time (anything guarded back on), and each lift window's start (Auto: off) and end (back on). A device
/// checks the events between its last run and now, and warns before each "on" whose blocks are off.
/// </summary>
public static class BlockRearm
{
    /// <summary>
    /// One event: at <see cref="At"/>, turn <see cref="Blocks"/> on or off (null = every guarded block of the device,
    /// see <see cref="BlockRules.Guarded"/>); a heads-up at <see cref="WarnAt"/> (null = none).
    /// </summary>
    public sealed record Event(DateTime At, bool TurnOn, IReadOnlyList<BlockRef>? Blocks, DateTime? WarnAt, string Why);

    /// <summary>The events of <paramref name="day"/>'s rules for <paramref name="device"/>, in time order.</summary>
    public static IReadOnlyList<Event> For(DayPlan plan, DateOnly day, string device)
    {
        BlockRules rules = plan.TemplateFor(day).Rules;
        DateTime start = day.ToDateTime(TimeOnly.MinValue);
        var events = new List<Event>();

        DateTime reenable = rules.ReenableAt == TimeOnly.MinValue ? start.AddDays(1) : start + rules.ReenableAt.ToTimeSpan();
        IReadOnlyList<BlockRef>? guarded = rules.Guarded?.Where(b => b.Device == device).ToList();
        if (guarded is null || guarded.Count > 0)
        {
            events.Add(new Event(reenable, true, guarded, Warn(reenable, rules), "the day's re-enable time"));
        }

        foreach (BlockLift lift in rules.Lifts)
        {
            List<BlockRef> mine = lift.Blocks.Where(b => b.Device == device).ToList();
            if (mine.Count == 0)
            {
                continue;
            }

            (DateTime from, DateTime to) = Window(start, lift);
            if (lift.Mode == BlockLift.Auto)
            {
                events.Add(new Event(from, false, mine, null, "a lift window starts"));
            }

            events.Add(new Event(to, true, mine, Warn(to, rules), "a lift window ends"));
        }

        return events.OrderBy(e => e.At).ToList();
    }

    /// <summary>The events of the days around that fall in (<paramref name="after"/>, <paramref name="until"/>].</summary>
    public static IReadOnlyList<Event> Between(DayPlan plan, string device, DateTime after, DateTime until) =>
        Around(plan, device, after, until).Where(e => e.At > after && e.At <= until).OrderBy(e => e.At).ToList();

    /// <summary>The heads-ups due in (<paramref name="after"/>, <paramref name="until"/>].</summary>
    public static IReadOnlyList<Event> WarningsBetween(DayPlan plan, string device, DateTime after, DateTime until) =>
        Around(plan, device, after, until.AddDays(1))
            .Where(e => e.WarnAt is DateTime at && at > after && at <= until)
            .OrderBy(e => e.WarnAt)
            .ToList();

    /// <summary>True when <paramref name="block"/> is in one of its lift windows at <paramref name="now"/>.</summary>
    public static bool InLift(DayPlan plan, BlockRef block, DateTime now)
    {
        foreach (DateOnly day in new[] { DateOnly.FromDateTime(now).AddDays(-1), DateOnly.FromDateTime(now) })
        {
            DateTime start = day.ToDateTime(TimeOnly.MinValue);
            foreach (BlockLift lift in plan.TemplateFor(day).Rules.Lifts.Where(l => l.Blocks.Any(b => b.Same(block))))
            {
                (DateTime from, DateTime to) = Window(start, lift);
                if (now >= from && now < to)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<Event> Around(DayPlan plan, string device, DateTime after, DateTime until)
    {
        DateOnly last = DateOnly.FromDateTime(until);
        for (DateOnly day = DateOnly.FromDateTime(after).AddDays(-1); day <= last; day = day.AddDays(1))
        {
            foreach (Event e in For(plan, day, device))
            {
                yield return e;
            }
        }
    }

    private static (DateTime From, DateTime To) Window(DateTime dayStart, BlockLift lift)
    {
        DateTime from = dayStart + lift.Start.ToTimeSpan();
        DateTime to = dayStart + lift.End.ToTimeSpan();
        return (from, to <= from ? to.AddDays(1) : to);
    }

    private static DateTime? Warn(DateTime at, BlockRules rules) =>
        rules.WarnMinutes is int minutes and > 0 ? at.AddMinutes(-minutes) : null;
}
