using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// How the user's days are meant to go (schema v5, replacing the weekly schedule of profiles, ADR 18; named templates and
/// a week map since schema v12, ADR 47): categories, named day templates, which template each weekday follows (week A,
/// optionally alternating with week B), and one-off days that replace a single date's template.
/// </summary>
/// <param name="Templates">The named templates; the first two migrated ones are <see cref="WorkdayTemplateId"/> and <see cref="OffDayTemplateId"/>.</param>
/// <param name="Week">Which template each weekday follows.</param>
public sealed record DayPlan(
    IReadOnlyList<DayCategory> Categories,
    IReadOnlyList<PlanTemplate> Templates,
    PlanWeek Week,
    IReadOnlyList<OneOffDay> OneOffDays)
{
    /// <summary>The migrated "Workday" template (schema v12): the same id everywhere it's created.</summary>
    public static Guid WorkdayTemplateId { get; } = Guid.Parse("9d3e5a20-0001-4e8a-b2c4-6f1d00000001");

    /// <summary>The "Off day" template, also the fallback for a date no map entry covers; the same id everywhere.</summary>
    public static Guid OffDayTemplateId { get; } = Guid.Parse("9d3e5a20-0002-4e8a-b2c4-6f1d00000002");

    /// <summary>The weekdays in the order the week map lists them (declared before <see cref="Default"/>, which uses it).</summary>
    public static IReadOnlyList<DayOfWeek> MondayFirst { get; } = new[]
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    // Fixed ids, so the starting plan is the same everywhere it's created.
    private static readonly DayCategory WorkCategory = new(Guid.Parse("8b1f0c52-0001-4c6e-9a51-3f2d7c000001"), "Work", "#2563EB", CategoryBehavior.Watch);
    private static readonly DayCategory ExtraWorkCategory = new(Guid.Parse("8b1f0c52-0002-4c6e-9a51-3f2d7c000002"), "Extra Work", "#7C3AED", CategoryBehavior.Watch);
    private static readonly DayCategory LunchCategory = new(Guid.Parse("8b1f0c52-0003-4c6e-9a51-3f2d7c000003"), "Lunch", "#CA8A04", CategoryBehavior.Rest);
    private static readonly DayCategory EveningsCategory = new(Guid.Parse("8b1f0c52-0004-4c6e-9a51-3f2d7c000004"), "Evenings", "#0D9488", CategoryBehavior.Rest);
    private static readonly DayCategory FreeTimeCategory = new(Guid.Parse("8b1f0c52-0005-4c6e-9a51-3f2d7c000005"), "Free time", "#16A34A", CategoryBehavior.Rest);
    private static readonly DayCategory SleepCategory = new(Guid.Parse("8b1f0c52-0006-4c6e-9a51-3f2d7c000006"), "Sleep", "#334155", CategoryBehavior.Sleep);

    /// <summary>Mail and chat windows (schema v6); the same id everywhere it's created.</summary>
    public static DayCategory CommsCategory { get; } = new(Guid.Parse("8b1f0c52-0007-4c6e-9a51-3f2d7c000007"), "Comms", "#0EA5E9", CategoryBehavior.Comms);

    /// <summary>The guarded warm-up before work (schema v7, ADR 31); the same id everywhere it's created. No parts by default.</summary>
    public static DayCategory PreWorkCategory { get; } = new(Guid.Parse("8b1f0c52-0008-4c6e-9a51-3f2d7c000008"), "Pre-Work", "#D97706", CategoryBehavior.PreWork);

    /// <summary>
    /// The starting plan: a 9-to-5 workday with two 15-minute comms windows (11:30, 16:00), lunch, evening extra work and
    /// sleep; free other days.
    /// </summary>
    public static DayPlan Default { get; } = FromWorkdayAndOffDay(
        new[] { WorkCategory, ExtraWorkCategory, LunchCategory, EveningsCategory, FreeTimeCategory, SleepCategory, CommsCategory, PreWorkCategory },
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
        new DayTemplate(new[]
        {
            new PlanPart(Guid.Parse("5c7a2e10-0001-4b3d-8f6e-1a9c40000001"), WorkCategory.Id, new TimeOnly(9, 0), new TimeOnly(11, 30)),
            new PlanPart(Guid.Parse("5c7a2e10-0008-4b3d-8f6e-1a9c40000008"), CommsCategory.Id, new TimeOnly(11, 30), new TimeOnly(11, 45)),
            new PlanPart(Guid.Parse("5c7a2e10-0009-4b3d-8f6e-1a9c40000009"), WorkCategory.Id, new TimeOnly(11, 45), new TimeOnly(12, 0)),
            new PlanPart(Guid.Parse("5c7a2e10-0002-4b3d-8f6e-1a9c40000002"), LunchCategory.Id, new TimeOnly(12, 0), new TimeOnly(13, 0)),
            new PlanPart(Guid.Parse("5c7a2e10-0003-4b3d-8f6e-1a9c40000003"), WorkCategory.Id, new TimeOnly(13, 0), new TimeOnly(16, 0)),
            new PlanPart(Guid.Parse("5c7a2e10-000a-4b3d-8f6e-1a9c4000000a"), CommsCategory.Id, new TimeOnly(16, 0), new TimeOnly(16, 15)),
            new PlanPart(Guid.Parse("5c7a2e10-000b-4b3d-8f6e-1a9c4000000b"), WorkCategory.Id, new TimeOnly(16, 15), new TimeOnly(17, 0)),
            new PlanPart(Guid.Parse("5c7a2e10-0004-4b3d-8f6e-1a9c40000004"), ExtraWorkCategory.Id, new TimeOnly(18, 0), new TimeOnly(20, 0)),
            new PlanPart(Guid.Parse("5c7a2e10-0005-4b3d-8f6e-1a9c40000005"), SleepCategory.Id, new TimeOnly(23, 0), new TimeOnly(7, 0)),
        }),
        new DayTemplate(new[]
        {
            new PlanPart(Guid.Parse("5c7a2e10-0006-4b3d-8f6e-1a9c40000006"), FreeTimeCategory.Id, new TimeOnly(10, 0), new TimeOnly(22, 0)),
            new PlanPart(Guid.Parse("5c7a2e10-0007-4b3d-8f6e-1a9c40000007"), SleepCategory.Id, new TimeOnly(0, 30), new TimeOnly(8, 30)),
        }),
        Array.Empty<OneOffDay>());

    /// <summary>
    /// A plan of the two classic templates (schema v11's shape): <paramref name="workdays"/> follow "Workday", every other
    /// weekday "Off day", week A only.
    /// </summary>
    public static DayPlan FromWorkdayAndOffDay(
        IReadOnlyList<DayCategory> categories,
        IEnumerable<DayOfWeek> workdays,
        DayTemplate workday,
        DayTemplate otherDay,
        IReadOnlyList<OneOffDay> oneOffDays)
    {
        HashSet<DayOfWeek> workdaySet = workdays.ToHashSet();
        return new DayPlan(
            categories,
            new[] { new PlanTemplate(WorkdayTemplateId, "Workday", workday), new PlanTemplate(OffDayTemplateId, "Off day", otherDay) },
            new PlanWeek(MondayFirst.Select(day => new WeekdayTemplate(day, workdaySet.Contains(day) ? WorkdayTemplateId : OffDayTemplateId)).ToList()),
            oneOffDays);
    }

    /// <summary>
    /// The "Workday" template as stored (the classic two-template view of the plan, until the editors know named
    /// templates); an empty template when it has been removed.
    /// </summary>
    [JsonIgnore]
    public DayTemplate Workday => TemplateById(WorkdayTemplateId) ?? DayTemplate.Empty;

    /// <summary>The "Off day" template as stored; see <see cref="Workday"/>.</summary>
    [JsonIgnore]
    public DayTemplate OtherDay => TemplateById(OffDayTemplateId) ?? DayTemplate.Empty;

    /// <summary>The weekdays of week A that follow the "Workday" template (the classic two-template view).</summary>
    [JsonIgnore]
    public IReadOnlyList<DayOfWeek> Workdays =>
        MondayFirst.Where(day => Week.A.Any(entry => entry.Day == day && entry.TemplateId == WorkdayTemplateId)).ToList();

    /// <summary>The plan with the "Workday" template replaced (added when it was removed).</summary>
    public DayPlan WithWorkday(DayTemplate template)
    {
        return WithTemplate(WorkdayTemplateId, "Workday", template);
    }

    /// <summary>The plan with the "Off day" template replaced (added when it was removed).</summary>
    public DayPlan WithOtherDay(DayTemplate template)
    {
        return WithTemplate(OffDayTemplateId, "Off day", template);
    }

    /// <summary>
    /// Week A with exactly <paramref name="workdays"/> following "Workday" (the classic two-template view): a weekday
    /// that follows "Workday" and isn't listed goes to "Off day"; one following any other template stays as it is unless
    /// listed.
    /// </summary>
    public DayPlan WithWorkdays(IEnumerable<DayOfWeek> workdays)
    {
        HashSet<DayOfWeek> workdaySet = workdays.ToHashSet();
        var week = new List<WeekdayTemplate>();
        foreach (DayOfWeek day in MondayFirst)
        {
            Guid current = Week.A.FirstOrDefault(entry => entry.Day == day)?.TemplateId ?? OffDayTemplateId;
            Guid next = workdaySet.Contains(day) ? WorkdayTemplateId : current == WorkdayTemplateId ? OffDayTemplateId : current;
            week.Add(new WeekdayTemplate(day, next));
        }

        return this with { Week = Week with { A = week } };
    }

    /// <summary>The template with <paramref name="id"/> as stored; null when there is none.</summary>
    public DayTemplate? TemplateById(Guid id)
    {
        return Templates.FirstOrDefault(template => template.Id == id)?.Template;
    }

    private DayPlan WithTemplate(Guid id, string name, DayTemplate template)
    {
        return Templates.Any(existing => existing.Id == id)
            ? this with { Templates = Templates.Select(existing => existing.Id == id ? existing with { Template = template } : existing).ToList() }
            : this with { Templates = [.. Templates, new PlanTemplate(id, name, template)] };
    }

    public DayCategory? Category(Guid id)
    {
        return Categories.FirstOrDefault(category => category.Id == id);
    }

    /// <summary>
    /// The template a date follows, in order: its one-off day, that week's (A or B) template for the weekday, the Off day
    /// template (empty when there is none). A Schedule template with only a work window comes with that window as its
    /// work part, so everything that reads parts sees it.
    /// </summary>
    public DayTemplate TemplateFor(DateOnly date)
    {
        OneOffDay? oneOff = OneOffDays.FirstOrDefault(day => day.Date == date);
        return (oneOff?.Template ?? WeekMapTemplate(date)).WithWorkWindowPart(Categories);
    }

    /// <summary>The template the week map gives a date, ignoring one-off days: what the date would follow if nothing replaced it.</summary>
    public DayTemplate WeekTemplateFor(DateOnly date)
    {
        return WeekMapTemplate(date).WithWorkWindowPart(Categories);
    }

    /// <summary>
    /// True on a workday, the one rule for every device and feature (the phone's DayPlan.isWorkday is the same): a
    /// one-off day with work in it, else a date whose week maps its weekday to the "Workday" template, or to a template of
    /// its own that has work (a Budget day, or work parts or a work window). A one-off day replaces a workday's template,
    /// so a one-off day off on a Monday isn't one. With <paramref name="withOneOffDays"/> off, the week map decides alone.
    /// </summary>
    public bool IsWorkday(DateOnly date, bool withOneOffDays = true)
    {
        OneOffDay? oneOff = withOneOffDays ? OneOffDays.FirstOrDefault(day => day.Date == date) : null;
        if (oneOff is not null)
        {
            return HasWork(oneOff.Template);
        }

        if (Week.TemplateIdFor(date) is not Guid id)
        {
            return false;
        }

        return IsWorkdayTemplate(id);
    }

    /// <summary>
    /// The template a plan's workdays (or, with <paramref name="workday"/> off, its other days) usually follow: the one
    /// the first such weekday of week A, Monday first, follows; null when there is none. Settings that keep one value
    /// for "workdays" and one for "other days" (sleep fallbacks, re-enable times) read it.
    /// </summary>
    public DayTemplate? TypicalTemplate(bool workday)
    {
        foreach (DayOfWeek day in MondayFirst)
        {
            Guid id = Week.A.FirstOrDefault(entry => entry.Day == day)?.TemplateId ?? OffDayTemplateId;
            if (IsWorkdayTemplate(id) == workday && TemplateById(id) is DayTemplate template)
            {
                return template;
            }
        }

        return null;
    }

    /// <summary>True when a day that follows template <paramref name="id"/> is a workday: the "Workday" template, or a template of its own with work.</summary>
    public bool IsWorkdayTemplate(Guid id)
    {
        return id == WorkdayTemplateId || (id != OffDayTemplateId && TemplateById(id) is DayTemplate template && HasWork(template));
    }

    private bool HasWork(DayTemplate template)
    {
        return template.Mode == DayMode.Budget
            || (template.Mode == DayMode.Schedule
                && template.WithWorkWindowPart(Categories).Parts.Any(part => Category(part.CategoryId)?.Behavior.IsWork() == true));
    }

    private DayTemplate WeekMapTemplate(DateOnly date)
    {
        return (Week.TemplateIdFor(date) is Guid id ? TemplateById(id) : null)
            ?? TemplateById(OffDayTemplateId)
            ?? DayTemplate.Empty;
    }

    /// <summary>
    /// Every part touching <paramref name="range"/>, placed on real dates and clipped to the range, in order. A part
    /// that crosses midnight starts the evening before the date it belongs to. Where parts of different dates overlap
    /// (yesterday's evening and tonight's sleep, which belongs to tomorrow), the later date's part wins and the earlier
    /// one keeps only what's outside it, so parts never overlap and a night is never cut short.
    /// </summary>
    public IReadOnlyList<PlannedPart> PartsIn(LocalWindow range)
    {
        var placed = new List<(PlannedPart Part, DateTime Day)>();
        // From the day before (its parts may run into the range) to the day after (its sleep starts the evening before).
        for (DateTime day = range.Start.Date.AddDays(-1); day <= range.End.Date.AddDays(1); day = day.AddDays(1))
        {
            foreach (PlanPart part in TemplateFor(DateOnly.FromDateTime(day)).Parts)
            {
                if (Category(part.CategoryId) is not DayCategory category)
                {
                    continue;
                }

                DateTime start = (part.CrossesMidnight ? day.AddDays(-1) : day) + part.Start.ToTimeSpan();
                DateTime end = day + TimeSpan.FromMinutes(part.EndMinutes);
                placed.Add((new PlannedPart(part, category, new LocalWindow(start, end)), day));
            }
        }

        var parts = new List<PlannedPart>();
        foreach ((PlannedPart planned, DateTime day) in placed)
        {
            // Parts of the same date never overlap (the validator sees to it); later dates take precedence.
            IEnumerable<LocalWindow> pieces = new[] { planned.Window };
            foreach ((PlannedPart later, DateTime _) in placed.Where(other => other.Day > day))
            {
                pieces = pieces.SelectMany(piece => Without(piece, later.Window)).ToList();
            }

            foreach (LocalWindow piece in pieces)
            {
                DateTime start = piece.Start > range.Start ? piece.Start : range.Start;
                DateTime end = piece.End < range.End ? piece.End : range.End;
                if (end > start)
                {
                    parts.Add(planned with { Window = new LocalWindow(start, end) });
                }
            }
        }

        return parts.OrderBy(part => part.Window.Start).ThenBy(part => part.Window.End).ThenBy(part => part.Part.Id).ToList();
    }

    /// <summary>
    /// The flexible break (e.g. lunch) on <paramref name="date"/>, placed on its date: its window, how long it lasts
    /// and its hint time; null when that day has none (the first one, when there are several).
    /// </summary>
    public FlexibleBreak? FlexibleBreakOn(DateOnly date)
    {
        DateTime day = date.ToDateTime(TimeOnly.MinValue);
        foreach (PlanPart part in TemplateFor(date).Parts.Where(part => part.IsFlexible && !part.CrossesMidnight))
        {
            if (Category(part.CategoryId) is DayCategory category)
            {
                var window = new LocalWindow(day + part.Start.ToTimeSpan(), day + TimeSpan.FromMinutes(part.EndMinutes));
                return new FlexibleBreak(part.Label ?? category.Name, category.Color, window, TimeSpan.FromMinutes(part.FlexMinutes!.Value),
                    part.HintAt is TimeOnly hint ? day + hint.ToTimeSpan() : null);
            }
        }

        return null;
    }

    /// <summary>The part at <paramref name="localTime"/>, or null when nothing is planned then.</summary>
    public PlannedPart? PartAt(DateTime localTime)
    {
        return PartsIn(new LocalWindow(localTime, localTime.AddMinutes(1)))
            .FirstOrDefault(part => part.Window.Contains(localTime));
    }

    /// <summary>What's left of <paramref name="window"/> outside <paramref name="hole"/>: nothing, one piece or two.</summary>
    private static IEnumerable<LocalWindow> Without(LocalWindow window, LocalWindow hole)
    {
        if (hole.End <= window.Start || hole.Start >= window.End)
        {
            yield return window;
            yield break;
        }

        if (hole.Start > window.Start)
        {
            yield return new LocalWindow(window.Start, hole.Start);
        }

        if (hole.End < window.End)
        {
            yield return new LocalWindow(hole.End, window.End);
        }
    }
}
