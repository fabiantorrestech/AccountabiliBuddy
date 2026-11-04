using System.Security.Cryptography;
using System.Text;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Lays classified calendar events over the stored day plan and returns the plan the guards read (ADR 58): the effective
/// plan, which exists only in memory. It is the stored plan with a one-off day for each date an event touches (that
/// date's own template, cut where an event takes over, plus the event's part, marked <see cref="PlanPart.FromEvent"/>), so
/// <see cref="DayWorkWindows"/>, <see cref="CommsWindows"/> and the timelines read it unchanged. Nothing here is saved: the
/// stored plan, its version hash and the sync file never see an event part.
/// <list type="bullet">
/// <item>Strictness: Watch 3, Comms 2, Rest 1, nothing 0. An event is laid over the stored plan piece by piece along what
/// the guards see there (<see cref="DayWorkWindows.PartsIn"/>, so a Budget day's stretch counts as Watch and a Free day as
/// nothing). Where parts overlap, the strictest is the base. A piece that is the same or stricter applies at once. A looser
/// one applies only when the app first saw the event at its current start, end, category and behaviour at least the
/// looser-change delay before it starts, and no session the user started is running; otherwise the plan stands there. A
/// Comms piece outside the stored Comms parts of a day that has some is looser, because it opens mail and chat. Where events
/// overlap, the stricter wins.</item>
/// <item>A Sleep part is never cut, and a Pre-Work part only by an event classified by hand, as a looser change (ADR 64); otherwise the piece of an event over one is dropped.</item>
/// <item>An event that crosses midnight splits per date. An existing one-off day is the base for its date.</item>
/// <item>Budget day: a Work event inside a Budget day's stretch never becomes a part. Under the looser rule it gives that
/// day (the one whose stretch holds the time) a <see cref="DayTemplate.BudgetCredit"/> for its time that has already passed,
/// less any time also counted as focus, so a meeting frees nothing before it happens and nothing counts twice. Without notice
/// it gives no credit but still wins overlaps. The stored budget, the focus totals and Stats are untouched (ADR 20).</item>
/// <item>Free day: a Work event adds a window; <see cref="DayWorkWindows"/> keeps the parts flagged <see cref="PlanPart.FromEvent"/>
/// when it strips a Free or Budget day's work parts.</item>
/// </list>
/// With nothing classified, or nothing that applies, the stored plan itself is returned.
/// </summary>
public static class CalendarDayOverlay
{
    private static readonly TimeSpan OldestEvent = TimeSpan.FromDays(8);

    public static DayPlan Apply(DayPlan stored, CalendarOverlayInput input)
    {
        // Events that ended more than 8 days ago are left out: no guard looks back that far and a calendar keeps months of them.
        DateTime oldest = TimeZoneInfo.ConvertTime(input.Now, input.Zone).DateTime - OldestEvent;
        IReadOnlyList<(CalendarEvent Event, DayCategory Category)> classified =
            EventCategoryResolver.Classified(input.Events.Where(calendarEvent => calendarEvent.LocalEnd >= oldest), stored, input.Classifications, input.Rules);
        return classified.Count == 0 ? stored : new Overlay(stored, input).Build(classified);
    }

    // KeepBase: an event the plan already agrees with (kept so it still wins an overlap); Part: the event's own part;
    // BudgetCredit: a Work event's time counted toward a Budget day.
    private enum RegionKind
    {
        KeepBase,
        Part,
        BudgetCredit,
    }

    // BudgetDate and Credited are for BudgetCredit regions: the Budget day whose stretch holds the time, and whether the notice was served.
    private sealed record Region(LocalWindow Window, RegionKind Kind, DayCategory Category, CalendarEvent Event, int Strictness)
    {
            /// <summary>The event was classified by hand and this piece lies over Pre-Work, which it lifts for its time (ADR 64).</summary>
            public bool LiftsPreWork { get; init; }

        public DateOnly? BudgetDate { get; init; }

        public bool Credited { get; init; }
    }

    private sealed class Overlay
    {
        private readonly DayPlan _stored;
        private readonly CalendarOverlayInput _input;
        private readonly Dictionary<(string, string), FirstKnownEntry> _known;
        private readonly DateTime _localNow;
        private readonly Dictionary<DateOnly, DayMode> _modes = new();
        private readonly Dictionary<DateOnly, bool> _storedComms = new();
        private readonly Dictionary<DateOnly, List<PlanPart>> _parts = new();
        private readonly HashSet<DateOnly> _changed = new();

        public Overlay(DayPlan stored, CalendarOverlayInput input)
        {
            _stored = stored;
            _input = input;
            _known = FirstKnownTimes.ByEvent(input.FirstKnown);
            _localNow = TimeZoneInfo.ConvertTime(input.Now, input.Zone).DateTime;
        }

        public DayPlan Build(IReadOnlyList<(CalendarEvent Event, DayCategory Category)> classified)
        {
            var regions = new List<Region>();
            foreach ((CalendarEvent calendarEvent, DayCategory category) in classified)
            {
                Collect(calendarEvent, category, regions);
            }

            List<Region> placed = StricterWins(regions);
            foreach (Region region in placed.Where(region => region.Kind == RegionKind.Part))
            {
                PlaceEventPart(region);
            }

            Dictionary<DateOnly, TimeSpan> credits = CreditByDay(placed);
            if (_changed.Count == 0 && credits.Count == 0)
            {
                return _stored;
            }

            var touched = new SortedSet<DateOnly>(_changed.Concat(credits.Keys));
            List<OneOffDay> oneOffDays = _stored.OneOffDays.Where(day => !touched.Contains(day.Date)).ToList();
            foreach (DateOnly date in touched)
            {
                DayTemplate template = _stored.TemplateFor(date);
                OneOffDay? existing = _stored.OneOffDays.FirstOrDefault(day => day.Date == date);
                oneOffDays.Add(new OneOffDay(
                    existing?.Id ?? StableGuid($"one-off|{date:yyyyMMdd}"),
                    date,
                    template with { Parts = PartsOf(date), WorkWindow = null, BudgetCredit = credits.GetValueOrDefault(date) },
                    existing?.Note));
            }

            return _stored with { OneOffDays = oneOffDays.OrderBy(day => day.Date).ToList() };
        }

        // The regions one event takes, piece by piece along what the guards see under it.
        private void Collect(CalendarEvent calendarEvent, DayCategory category, List<Region> regions)
        {
            int strictness = CategoryStrictness.Of(category.Behavior);
            bool byHand = EventCategoryResolver.IsChosenByHand(calendarEvent, _input.Classifications);
            bool? looserAllowed = null;
            bool LooserAllowed() => looserAllowed ??= IsLooserAllowed(calendarEvent, category);

            var whole = new LocalWindow(calendarEvent.LocalStart, calendarEvent.LocalEnd);
            IReadOnlyList<PlannedPart> seen = DayWorkWindows.PartsIn(_stored, whole, _localNow, null, _input.NightFallback);
            foreach ((LocalWindow segment, PlannedPart? basePart) in Segments(whole, seen))
            {
                bool overPreWork = basePart?.Category.Behavior == CategoryBehavior.PreWork;
                if (basePart?.Category.Behavior == CategoryBehavior.Sleep || (overPreWork && !byHand))
                {
                    continue;
                }

                // Lifting Pre-Work is always a looser change, so it counts as above every category an event can have.
                int baseStrictness = overPreWork ? int.MaxValue : basePart is null ? 0 : CategoryStrictness.Of(basePart.Category.Behavior);
                bool inBudgetStretch = basePart?.Part.Id == DayWorkWindows.BudgetWindowPartId;
                foreach (LocalWindow piece in ByDate(segment))
                {
                    Region? region = RegionFor(piece, category, calendarEvent, strictness, baseStrictness, basePart, inBudgetStretch, LooserAllowed);
                    if (region is not null)
                    {
                        regions.Add(region with { LiftsPreWork = overPreWork });
                    }
                }
            }
        }

        private Region? RegionFor(
            LocalWindow piece, DayCategory category, CalendarEvent calendarEvent, int strictness, int baseStrictness,
            PlannedPart? basePart, bool inBudgetStretch, Func<bool> looserAllowed)
        {
            DateOnly date = DateOnly.FromDateTime(piece.Start);
            if (category.Behavior == CategoryBehavior.Watch)
            {
                // A Work event inside a Budget day's stretch is never a part; it counts toward that day's budget, which is a
                // looser change. Without the notice it gives no credit but still takes part in overlaps.
                if (inBudgetStretch && DayWorkWindows.BudgetDayHolding(_stored, piece.Start, _input.NightFallback) is DateOnly holder)
                {
                    return new Region(piece, RegionKind.BudgetCredit, category, calendarEvent, strictness) { BudgetDate = holder, Credited = looserAllowed() };
                }

                if (ModeOf(date) == DayMode.Budget)
                {
                    return null;
                }
            }

            // On a day with Comms parts of its own, mail and chat are closed outside them: a Comms piece there opens them, whatever it covers.
            if (category.Behavior == CategoryBehavior.Comms && basePart?.Category.Behavior != CategoryBehavior.Comms && HasStoredComms(date))
            {
                return looserAllowed() ? new Region(piece, RegionKind.Part, category, calendarEvent, strictness) : null;
            }

            if (strictness > baseStrictness)
            {
                return new Region(piece, RegionKind.Part, category, calendarEvent, strictness);
            }

            if (strictness == baseStrictness)
            {
                return new Region(piece, basePart?.Category.Id == category.Id ? RegionKind.KeepBase : RegionKind.Part, category, calendarEvent, strictness);
            }

            return looserAllowed() ? new Region(piece, RegionKind.Part, category, calendarEvent, strictness) : null;
        }

        // Overlapping events: the stricter one keeps the time they share.
        private static List<Region> StricterWins(List<Region> regions)
        {
            var placed = new List<Region>();
            foreach (Region region in regions
                         .OrderByDescending(region => region.Strictness)
                         .ThenBy(region => region.Kind)
                         .ThenBy(region => region.Window.Start)
                         .ThenBy(region => region.Event.Id, StringComparer.Ordinal))
            {
                foreach (LocalWindow free in DayTimeline.Subtract([region.Window], placed.Select(other => other.Window)))
                {
                    placed.Add(region with { Window = free });
                }
            }

            return placed;
        }

        // The Budget credit of each Budget day: the time its credited Work events have already used, less time counted as focus.
        private Dictionary<DateOnly, TimeSpan> CreditByDay(List<Region> placed)
        {
            var credits = new Dictionary<DateOnly, TimeSpan>();
            var untilNow = new LocalWindow(DateTime.MinValue, _localNow);
            IReadOnlyList<LocalWindow> focus = _input.CountedFocus ?? [];
            foreach (IGrouping<DateOnly, Region> day in placed
                         .Where(region => region.Kind == RegionKind.BudgetCredit && region.Credited && region.BudgetDate is not null)
                         .GroupBy(region => region.BudgetDate!.Value))
            {
                List<LocalWindow> passed = day.Select(region => DayTimeline.Clip(region.Window, untilNow)).OfType<LocalWindow>().ToList();
                TimeSpan counted = DayTimeline.Subtract(passed, focus).Aggregate(TimeSpan.Zero, (sum, window) => sum + (window.End - window.Start));
                int minutes = (int)Math.Floor(counted.TotalMinutes);
                if (minutes > 0)
                {
                    credits[day.Key] = TimeSpan.FromMinutes(minutes);
                }
            }

            return credits;
        }

        private bool HasStoredComms(DateOnly date)
        {
            if (!_storedComms.TryGetValue(date, out bool has))
            {
                DateTime start = date.ToDateTime(TimeOnly.MinValue);
                has = DayWorkWindows.PartsIn(_stored, new LocalWindow(start, start.AddDays(1)), _localNow, null, _input.NightFallback)
                    .Any(part => part.Category.Behavior == CategoryBehavior.Comms);
                _storedComms[date] = has;
            }

            return has;
        }

        private void PlaceEventPart(Region region)
        {
            DateOnly date = DateOnly.FromDateTime(region.Window.Start);
            Cut(date, region.Window, region.LiftsPreWork);
            Cut(date.AddDays(1), region.Window, region.LiftsPreWork);

            var part = new PlanPart(
                StableGuid($"event|{region.Event.CalendarId}|{region.Event.Id}|{region.Window.Start:yyyyMMddTHHmm}"),
                region.Category.Id,
                TimeOnly.FromDateTime(region.Window.Start),
                TimeOnly.FromDateTime(region.Window.End))
            {
                FromEvent = true,
            };
            PartsOf(date).Add(part);
            _changed.Add(date);
        }

        // Takes <paramref name="hole"/> out of the parts that belong to <paramref name="owner"/> (a part belongs to the date it
        // ends on, so the date's evening may be held by the next date's night). What is left of a part is placed again.
        private void Cut(DateOnly owner, LocalWindow hole, bool liftsPreWork = false)
        {
            List<PlanPart> parts = PartsOf(owner);
            foreach (PlanPart part in parts.ToList())
            {
                if (part.FromEvent || IsProtected(part, liftsPreWork))
                {
                    continue;
                }

                LocalWindow window = RawWindow(part, owner);
                if (window.End <= hole.Start || window.Start >= hole.End)
                {
                    continue;
                }

                parts.RemoveAll(other => ReferenceEquals(other, part));
                _changed.Add(owner);
                int index = 0;
                foreach (LocalWindow piece in Remainder(window, hole))
                {
                    Place(part, piece, owner, index++);
                }
            }
        }

        // A piece of a part goes back to the date it belongs to: a piece that ends before its owner's midnight is an evening
        // of the day before; a piece across midnight stays a part that crosses it.
        private void Place(PlanPart original, LocalWindow piece, DateOnly owner, int index)
        {
            DateOnly home = piece.End <= owner.ToDateTime(TimeOnly.MinValue) ? owner.AddDays(-1) : owner;
            if (home != owner)
            {
                // The next date's part took precedence over this date's evening before; it still does.
                Cut(home, piece);
            }

            PlanPart placed = original with
            {
                Id = index == 0 ? original.Id : StableGuid($"{original.Id:N}|{index}"),
                Start = TimeOnly.FromDateTime(piece.Start),
                End = TimeOnly.FromDateTime(piece.End),
            };
            PartsOf(home).Add(placed);
            _changed.Add(home);
        }

        private bool IsProtected(PlanPart part, bool liftsPreWork)
        {
            CategoryBehavior? behavior = _stored.Category(part.CategoryId)?.Behavior;
            return behavior == CategoryBehavior.Sleep || (behavior == CategoryBehavior.PreWork && !liftsPreWork);
        }

        // A date's template as the guards see it (a Free or Budget day without its work parts), the parts the overlay edits.
        private List<PlanPart> PartsOf(DateOnly date)
        {
            if (!_parts.TryGetValue(date, out List<PlanPart>? parts))
            {
                parts = DayWorkWindows.Guarded(_stored, _stored.TemplateFor(date)).Parts.ToList();
                _parts[date] = parts;
            }

            return parts;
        }

        private DayMode ModeOf(DateOnly date)
        {
            if (!_modes.TryGetValue(date, out DayMode mode))
            {
                mode = _stored.TemplateFor(date).Mode;
                _modes[date] = mode;
            }

            return mode;
        }

        // A looser change counts when the app first saw the event as it is now at least the delay before it starts, and no
        // session the user started is running.
        private bool IsLooserAllowed(CalendarEvent calendarEvent, DayCategory category)
        {
            if (_input.SessionRunning || FirstKnownTimes.KnownAt(_known, calendarEvent, category) is not DateTimeOffset knownAt)
            {
                return false;
            }

            DateTime start = DateTime.SpecifyKind(calendarEvent.LocalStart, DateTimeKind.Unspecified);
            var startsAt = new DateTimeOffset(start, _input.Zone.GetUtcOffset(start));
            return knownAt + _input.LooseningDelay <= startsAt;
        }
    }

    // The window cut into stretches by what the guards see under it, in order. Where parts overlap (a flexible lunch inside the
    // work), the strictest is the base, a Sleep or Pre-Work part above all; a stretch with no part has none.
    private static IEnumerable<(LocalWindow Window, PlannedPart? Part)> Segments(LocalWindow whole, IReadOnlyList<PlannedPart> parts)
    {
        var edges = new SortedSet<DateTime> { whole.Start, whole.End };
        foreach (PlannedPart part in parts)
        {
            edges.Add(part.Window.Start > whole.Start ? part.Window.Start : whole.Start);
            edges.Add(part.Window.End < whole.End ? part.Window.End : whole.End);
        }

        DateTime[] cuts = edges.ToArray();
        PlannedPart? current = null;
        DateTime from = cuts[0];
        for (int index = 0; index < cuts.Length - 1; index++)
        {
            DateTime start = cuts[index];
            DateTime end = cuts[index + 1];
            PlannedPart? chosen = parts
                .Where(part => part.Window.Start <= start && part.Window.End >= end)
                .OrderByDescending(Rank)
                .ThenBy(part => part.Window.Start)
                .ThenBy(part => part.Part.Id)
                .FirstOrDefault();
            if (index > 0 && !ReferenceEquals(chosen, current))
            {
                yield return (new LocalWindow(from, start), current);
                from = start;
            }

            current = chosen;
        }

        yield return (new LocalWindow(from, whole.End), current);
    }

    private static int Rank(PlannedPart part)
    {
        return part.Category.Behavior is CategoryBehavior.Sleep or CategoryBehavior.PreWork ? 4 : CategoryStrictness.Of(part.Category.Behavior);
    }

    private static IEnumerable<LocalWindow> ByDate(LocalWindow window)
    {
        DateTime start = window.Start;
        while (start < window.End)
        {
            DateTime nextMidnight = start.Date.AddDays(1);
            DateTime end = nextMidnight < window.End ? nextMidnight : window.End;
            yield return new LocalWindow(start, end);
            start = end;
        }
    }

    private static IEnumerable<LocalWindow> Remainder(LocalWindow window, LocalWindow hole)
    {
        if (hole.Start > window.Start)
        {
            yield return new LocalWindow(window.Start, hole.Start);
        }

        if (hole.End < window.End)
        {
            yield return new LocalWindow(hole.End, window.End);
        }
    }

    // Where a part of <paramref name="owner"/>'s template lies on the calendar: a part that crosses midnight starts the evening before.
    private static LocalWindow RawWindow(PlanPart part, DateOnly owner)
    {
        DateTime midnight = owner.ToDateTime(TimeOnly.MinValue);
        DateTime start = (part.CrossesMidnight ? midnight.AddDays(-1) : midnight) + part.Start.ToTimeSpan();
        return new LocalWindow(start, midnight + TimeSpan.FromMinutes(part.EndMinutes));
    }

    // The same text gives the same id on every run, so an event's part keeps its id from one minute to the next.
    private static Guid StableGuid(string text)
    {
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
    }
}
