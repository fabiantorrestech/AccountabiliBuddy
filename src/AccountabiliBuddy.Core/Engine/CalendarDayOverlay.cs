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
/// nothing). A piece that is the same or stricter applies at once. A looser one applies only when the app first saw the
/// event at its current start, end and category at least the looser-change delay before it starts, and no session the user
/// started is running; otherwise the plan stands there. Where events overlap, the stricter wins.</item>
/// <item>A Sleep or Pre-Work part is never cut: the piece of an event over one is dropped.</item>
/// <item>An event that crosses midnight splits per date. An existing one-off day is the base for its date.</item>
/// <item>Budget day: a Work event never becomes a part. Under the looser rule it lowers that date's budget by its time
/// that has already passed inside the day's stretch, so a meeting frees nothing before it happens. The stored budget, the
/// focus totals and Stats are untouched (ADR 20).</item>
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

    private sealed record Region(LocalWindow Window, RegionKind Kind, DayCategory Category, CalendarEvent Event, int Strictness);

    private sealed class Overlay
    {
        private readonly DayPlan _stored;
        private readonly CalendarOverlayInput _input;
        private readonly Dictionary<(string, string), FirstKnownEntry> _known;
        private readonly DateTime _localNow;
        private readonly Dictionary<DateOnly, DayMode> _modes = new();
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

            Dictionary<DateOnly, int> credits = CreditMinutes(placed);
            if (_changed.Count == 0 && credits.Count == 0)
            {
                return _stored;
            }

            var touched = new SortedSet<DateOnly>(_changed.Concat(credits.Keys));
            List<OneOffDay> oneOffDays = _stored.OneOffDays.Where(day => !touched.Contains(day.Date)).ToList();
            foreach (DateOnly date in touched)
            {
                DayTemplate template = _stored.TemplateFor(date);
                int? budget = template.BudgetMinutes is int minutes && credits.TryGetValue(date, out int credit) ? Math.Max(0, minutes - credit) : template.BudgetMinutes;
                OneOffDay? existing = _stored.OneOffDays.FirstOrDefault(day => day.Date == date);
                oneOffDays.Add(new OneOffDay(
                    existing?.Id ?? StableGuid($"one-off|{date:yyyyMMdd}"),
                    date,
                    template with { Parts = PartsOf(date), WorkWindow = null, BudgetMinutes = budget },
                    existing?.Note));
            }

            return _stored with { OneOffDays = oneOffDays.OrderBy(day => day.Date).ToList() };
        }

        // The regions one event takes, piece by piece along what the guards see under it.
        private void Collect(CalendarEvent calendarEvent, DayCategory category, List<Region> regions)
        {
            int strictness = Strictness(category.Behavior);
            bool? looserAllowed = null;
            bool LooserAllowed() => looserAllowed ??= IsLooserAllowed(calendarEvent, category);

            var whole = new LocalWindow(calendarEvent.LocalStart, calendarEvent.LocalEnd);
            IReadOnlyList<PlannedPart> seen = DayWorkWindows.PartsIn(_stored, whole, _localNow, null, _input.NightFallback);
            foreach ((LocalWindow segment, PlannedPart? basePart) in Segments(whole, seen))
            {
                if (basePart?.Category.Behavior is CategoryBehavior.Sleep or CategoryBehavior.PreWork)
                {
                    continue;
                }

                int baseStrictness = basePart is null ? 0 : Strictness(basePart.Category.Behavior);
                bool inBudgetStretch = basePart?.Part.Id == DayWorkWindows.BudgetWindowPartId;
                foreach (LocalWindow piece in ByDate(segment))
                {
                    Region? region = RegionFor(piece, category, calendarEvent, strictness, baseStrictness, basePart, inBudgetStretch, LooserAllowed);
                    if (region is not null)
                    {
                        regions.Add(region);
                    }
                }
            }
        }

        private Region? RegionFor(
            LocalWindow piece, DayCategory category, CalendarEvent calendarEvent, int strictness, int baseStrictness,
            PlannedPart? basePart, bool inBudgetStretch, Func<bool> looserAllowed)
        {
            if (category.Behavior == CategoryBehavior.Watch && ModeOf(DateOnly.FromDateTime(piece.Start)) == DayMode.Budget)
            {
                // A Budget day's Work event is never a part; inside the stretch it counts toward the budget, which is a looser change.
                return inBudgetStretch && looserAllowed() ? new Region(piece, RegionKind.BudgetCredit, category, calendarEvent, strictness) : null;
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

        // The Budget minutes each date's Work events have used up so far (whole minutes, only time before now).
        private Dictionary<DateOnly, int> CreditMinutes(List<Region> placed)
        {
            var credits = new Dictionary<DateOnly, int>();
            var untilNow = new LocalWindow(DateTime.MinValue, _localNow);
            foreach (IGrouping<DateOnly, Region> day in placed.Where(region => region.Kind == RegionKind.BudgetCredit).GroupBy(region => DateOnly.FromDateTime(region.Window.Start)))
            {
                TimeSpan passed = day
                    .Select(region => DayTimeline.Clip(region.Window, untilNow))
                    .OfType<LocalWindow>()
                    .Aggregate(TimeSpan.Zero, (sum, window) => sum + (window.End - window.Start));
                int minutes = (int)Math.Floor(passed.TotalMinutes);
                if (minutes > 0)
                {
                    credits[day.Key] = minutes;
                }
            }

            return credits;
        }

        private void PlaceEventPart(Region region)
        {
            DateOnly date = DateOnly.FromDateTime(region.Window.Start);
            Cut(date, region.Window);
            Cut(date.AddDays(1), region.Window);

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
        private void Cut(DateOnly owner, LocalWindow hole)
        {
            List<PlanPart> parts = PartsOf(owner);
            foreach (PlanPart part in parts.ToList())
            {
                if (part.FromEvent || IsProtected(part))
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

        private bool IsProtected(PlanPart part)
        {
            return _stored.Category(part.CategoryId)?.Behavior is CategoryBehavior.Sleep or CategoryBehavior.PreWork;
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
            if (_input.SessionRunning || FirstKnownTimes.KnownAt(_known, calendarEvent, category.Id) is not DateTimeOffset knownAt)
            {
                return false;
            }

            DateTime start = DateTime.SpecifyKind(calendarEvent.LocalStart, DateTimeKind.Unspecified);
            var startsAt = new DateTimeOffset(start, _input.Zone.GetUtcOffset(start));
            return knownAt + _input.LooseningDelay <= startsAt;
        }
    }

    private static int Strictness(CategoryBehavior behavior)
    {
        return behavior switch
        {
            CategoryBehavior.Watch => 3,
            CategoryBehavior.Comms => 2,
            CategoryBehavior.Rest => 1,
            _ => 0,
        };
    }

    // The window cut into stretches by what the guards see under it, in order; a stretch with no part has none.
    private static IEnumerable<(LocalWindow Window, PlannedPart? Part)> Segments(LocalWindow whole, IReadOnlyList<PlannedPart> parts)
    {
        DateTime cursor = whole.Start;
        foreach (PlannedPart part in parts.OrderBy(part => part.Window.Start))
        {
            if (part.Window.End <= cursor)
            {
                continue;
            }

            DateTime start = part.Window.Start > cursor ? part.Window.Start : cursor;
            if (start > cursor)
            {
                yield return (new LocalWindow(cursor, start), null);
            }

            yield return (new LocalWindow(start, part.Window.End), part);
            cursor = part.Window.End;
        }

        if (cursor < whole.End)
        {
            yield return (new LocalWindow(cursor, whole.End), null);
        }
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
