using System.Globalization;
using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Builds the Overview-style views of the Stats page (Overview, Drift, Phone, Sleep) from <see cref="StatsData"/>. Each
/// returns null when its range has nothing to show, so the page says so instead of drawing zeros that were never measured.
/// </summary>
internal static class StatsBarPanels
{
    private const double GoalHeadroom = 1.25;

    public static StatsBarPanel? Overview(StatsData data, StatsLookups lookups, StatsRange range, DateOnly selected)
    {
        return range == StatsRange.Day ? OverviewDay(data, selected) : OverviewDays(data, lookups, range);
    }

    private static StatsBarPanel? OverviewDays(StatsData data, StatsLookups lookups, StatsRange range)
    {
        (DateOnly from, DateOnly to) = StatsFormat.Bounds(range, data.Today);
        IReadOnlyList<DeepWorkDay> past = data.DaysBetween(from, to);
        if (past.All(day => day.Focused <= TimeSpan.Zero))
        {
            return null;
        }

        TimeSpan total = past.Aggregate(TimeSpan.Zero, (sum, day) => sum + day.Focused);
        double scale = Math.Max(data.Goal.TotalMinutes * GoalHeadroom, past.Max(day => day.Focused.TotalMinutes));
        var bars = new List<StatsBar>();
        for (DateOnly date = from; date <= to; date = date.AddDays(1))
        {
            DeepWorkDay? day = data.DayOf(date);
            bool noBar = date > data.Today || day is null;
            TimeSpan focused = day?.Focused ?? TimeSpan.Zero;
            bool met = focused >= data.Goal;
            StatsBarKind kind = met ? StatsBarKind.Met : date == data.Today ? StatsBarKind.Accent : StatsBarKind.Plan;
            string tip = $"{date.ToString("ddd, MMM d", CultureInfo.CurrentCulture)} · {StatsFormat.Duration(focused)}{(met ? " · goal met" : string.Empty)}";
            bars.Add(new StatsBar(
                range == StatsRange.Week ? StatsFormat.WeekdayLetter(date) : date.Day.ToString(CultureInfo.CurrentCulture),
                focused.TotalMinutes / scale, kind, date == data.Today, noBar, tip, noBar ? null : date));
        }

        ConsistencySummary summary = ConsistencyStats.Summarize(past, data.Goal, lookups.IsWorkday, data.Today);
        string note = summary.WorkdaysCounted > 0
            ? $"deep work · {summary.WorkdaysMet} of {summary.WorkdaysCounted} workdays met"
            : "deep work";
        return new StatsBarPanel(
            StatsFormat.HoursMinutes(total), note, bars, data.Goal.TotalMinutes / scale, $"goal {StatsFormat.Duration(data.Goal)}",
            OverviewCards(data, from, to));
    }

    // One bar per hour of the selected day; a full bar is a full hour.
    private static StatsBarPanel? OverviewDay(StatsData data, DateOnly day)
    {
        DateTime start = day.ToDateTime(TimeOnly.MinValue);
        TimeSpan total = data.DayOf(day)?.Focused ?? TimeSpan.Zero;
        if (total <= TimeSpan.Zero)
        {
            return null;
        }

        var bars = new List<StatsBar>();
        for (int hour = 0; hour < 24; hour++)
        {
            var window = new LocalWindow(start.AddHours(hour), start.AddHours(hour + 1));
            double minutes = data.Windows.Select(focus => DayTimeline.Clip(focus, window)).OfType<LocalWindow>()
                .Sum(piece => (piece.End - piece.Start).TotalMinutes);
            bool inFuture = day == data.Today && window.Start > data.NowLocal;
            bool now = day == data.Today && window.Contains(data.NowLocal);
            string tip = $"{window.Start.ToString("t", CultureInfo.CurrentCulture)} · {StatsFormat.Duration(TimeSpan.FromMinutes(Math.Round(minutes)))}";
            bars.Add(new StatsBar(
                StatsFormat.HourShort(hour), minutes / 60, minutes <= 0 ? StatsBarKind.Plan : now ? StatsBarKind.Accent : StatsBarKind.Met,
                IsToday: false, inFuture, tip));
        }

        bool met = total >= data.Goal;
        string note = met ? "deep work · goal met" : $"deep work · {StatsFormat.Duration(data.Goal - total)} to go";
        return new StatsBarPanel(StatsFormat.HoursMinutes(total), note, bars, double.NaN, null, OverviewCards(data, day, day));
    }

    // Phone, Sleep and Drift: only the ones that have something to say.
    private static IReadOnlyList<StatsCard> OverviewCards(StatsData data, DateOnly from, DateOnly to)
    {
        var cards = new List<StatsCard>();
        if (PhoneCard(data, from, to) is { } phone)
        {
            cards.Add(phone);
        }

        if (SleepCard(data) is { } sleep)
        {
            cards.Add(sleep);
        }

        cards.Add(DriftCard(data, from, to));
        return cards;
    }

    private static StatsCard? PhoneCard(StatsData data, DateOnly from, DateOnly to)
    {
        IReadOnlyList<SyncPickup> pickups = data.PhoneCounts ? data.PickupsBetween(from, to) : [];
        if (pickups.Count == 0)
        {
            return null;
        }

        return new StatsCard(
            "Phone", pickups.Sum(pickup => pickup.Count).ToString(CultureInfo.CurrentCulture),
            $"pickups during sessions · longest down {Down(pickups.Max(pickup => pickup.LongestDownMinutes))}", StatsViewKind.Phone);
    }

    private static StatsCard? SleepCard(StatsData data)
    {
        if (Sleep(data.Bedtime) is not { } sleep)
        {
            return null;
        }

        string detail = sleep.Asked > 0 ? $"bedtime kept {sleep.Made} of {sleep.Asked} this week" : $"{sleep.Nights} nights measured";
        return new StatsCard("Sleep, avg", StatsFormat.HoursMinutes(sleep.Slept), detail, StatsViewKind.Sleep);
    }

    private static StatsCard DriftCard(StatsData data, DateOnly from, DateOnly to)
    {
        IReadOnlyList<DriftStretch> visits = data.DriftBetween(from, to);
        return visits.Count == 0
            ? new StatsCard("Drift", "0", "no drift in your sessions", StatsViewKind.Drift)
            : new StatsCard("Drift", visits.Count.ToString(CultureInfo.CurrentCulture), $"stretches off track · {StatsFormat.Duration(Time(visits))}", StatsViewKind.Drift);
    }

    public static StatsBarPanel? Drift(StatsData data, StatsLookups lookups, StatsRange range)
    {
        (DateOnly from, DateOnly to) = StatsFormat.Bounds(range, data.Today);
        IReadOnlyList<DeepWorkDay> past = data.DaysBetween(from, to);
        IReadOnlyList<DriftStretch> visits = data.DriftBetween(from, to);
        if (past.All(day => day.Focused <= TimeSpan.Zero) && visits.Count == 0)
        {
            return null;
        }

        Dictionary<DateOnly, TimeSpan> perDay = visits
            .GroupBy(visit => data.DateOf(visit.Start))
            .ToDictionary(group => group.Key, group => Time(group.ToList()));
        double scale = Math.Max(30, perDay.Values.DefaultIfEmpty().Max(time => time.TotalMinutes));
        var bars = new List<StatsBar>();
        for (DateOnly date = from; date <= to; date = date.AddDays(1))
        {
            bool noBar = date > data.Today || data.DayOf(date) is null;
            TimeSpan drift = perDay.GetValueOrDefault(date);
            bars.Add(new StatsBar(
                range == StatsRange.Week ? StatsFormat.WeekdayLetter(date) : date.Day.ToString(CultureInfo.CurrentCulture),
                drift.TotalMinutes / scale, StatsBarKind.Drift, date == data.Today, noBar,
                $"{date.ToString("ddd, MMM d", CultureInfo.CurrentCulture)} · {StatsFormat.Duration(drift)} off track", noBar ? null : date));
        }

        var cards = new List<StatsCard>();
        if (visits.Count > 0)
        {
            var top = visits.GroupBy(visit => lookups.SiteName(visit.Site), StringComparer.OrdinalIgnoreCase)
                .Select(group => (Name: group.Key, Time: Time(group.ToList()), Count: group.Count()))
                .OrderByDescending(site => site.Time).First();
            cards.Add(new StatsCard("Drifted to most", top.Name, $"{StatsFormat.Duration(top.Time)} · {top.Count} {Plural(top.Count, "visit")}"));
            KeyValuePair<DateOnly, TimeSpan> worst = perDay.OrderByDescending(pair => pair.Value).First();
            cards.Add(new StatsCard("Worst day", worst.Key.ToString("ddd, MMM d", CultureInfo.CurrentCulture), StatsFormat.Duration(worst.Value)));
            var hour = visits.GroupBy(visit => TimeZoneInfo.ConvertTime(visit.Start, data.Zone).Hour)
                .Select(group => (Hour: group.Key, Count: group.Count())).OrderByDescending(group => group.Count).First();
            cards.Add(new StatsCard("Busiest hour", StatsFormat.HourRange(hour.Hour, hour.Hour + 1), $"{hour.Count} {Plural(hour.Count, "stretch", "stretches")} began then"));
        }

        string note = visits.Count == 0 ? "no drift in your sessions" : $"drift stretches · {StatsFormat.Duration(Time(visits))} off track in sessions";
        return new StatsBarPanel(visits.Count.ToString(CultureInfo.CurrentCulture), note, bars, double.NaN, null, cards);
    }

    public static StatsBarPanel? Phone(StatsData data, StatsRange range)
    {
        (DateOnly from, DateOnly to) = StatsFormat.Bounds(range, data.Today);
        IReadOnlyList<SyncPickup> pickups = data.PhoneCounts ? data.PickupsBetween(from, to) : [];
        if (pickups.Count == 0)
        {
            return null;
        }

        Dictionary<DateOnly, int> perDay = pickups.GroupBy(pickup => data.DateOf(pickup.SessionStart))
            .ToDictionary(group => group.Key, group => group.Sum(pickup => pickup.Count));
        double scale = Math.Max(5, perDay.Values.Max());
        var bars = new List<StatsBar>();
        for (DateOnly date = from; date <= to; date = date.AddDays(1))
        {
            bool noBar = date > data.Today || data.DayOf(date) is null;
            int count = perDay.GetValueOrDefault(date);
            bars.Add(new StatsBar(
                range == StatsRange.Week ? StatsFormat.WeekdayLetter(date) : date.Day.ToString(CultureInfo.CurrentCulture),
                count / scale, StatsBarKind.Plan, date == data.Today, noBar,
                $"{date.ToString("ddd, MMM d", CultureInfo.CurrentCulture)} · {count} {Plural(count, "pickup")}", noBar ? null : date));
        }

        int total = pickups.Sum(pickup => pickup.Count);
        int free = pickups.Count(pickup => pickup.Count == 0);
        var cards = new List<StatsCard>
        {
            new("Phone-free sessions", $"{free} of {pickups.Count}", "sessions with no pickups"),
            new("Longest down", Down(pickups.Max(pickup => pickup.LongestDownMinutes)), "in one session"),
            new("Per session", (total / (double)pickups.Count).ToString("0.#", CultureInfo.CurrentCulture), "pickups on average"),
        };
        return new StatsBarPanel(
            total.ToString(CultureInfo.CurrentCulture), $"pickups during sessions · longest down {Down(pickups.Max(pickup => pickup.LongestDownMinutes))}",
            bars, double.NaN, null, cards);
    }

    public static StatsBarPanel? SleepView(StatsData data)
    {
        if (Sleep(data.Bedtime) is not { } sleep)
        {
            return null;
        }

        var cards = new List<StatsCard>();
        AddNights(cards, "Workday nights", data.Bedtime!.Workdays);
        AddNights(cards, "Other nights", data.Bedtime.OffDays);
        if (sleep.Asked > 0)
        {
            cards.Add(new StatsCard("Made it this week", $"{sleep.Made} of {sleep.Asked}", "nights you kept the bedtime asked"));
        }

        return new StatsBarPanel(StatsFormat.HoursMinutes(sleep.Slept), $"average sleep · {sleep.Nights} nights measured by your phone", null, double.NaN, null, cards);
    }

    private static void AddNights(List<StatsCard> cards, string title, SyncSmartLevel? level)
    {
        if (level is not { AverageSleptMinutes: int slept, Nights: > 0 })
        {
            return;
        }

        string asleep = level.AverageAsleep is { } at && TimeOnly.TryParse(at, CultureInfo.InvariantCulture, out TimeOnly time)
            ? $"asleep around {time.ToString("h:mm tt", CultureInfo.InvariantCulture)} · "
            : string.Empty;
        cards.Add(new StatsCard(title, StatsFormat.HoursMinutes(TimeSpan.FromMinutes(slept)), $"{asleep}{level.Nights} {Plural(level.Nights, "night")}"));
    }

    /// <summary>The phone's smart bedtime added up over its kinds of night; null when it measured no sleep.</summary>
    public static (TimeSpan Slept, int Nights, int Made, int Asked)? Sleep(SyncSmartBedtime? bedtime)
    {
        List<SyncSmartLevel> measured = new[] { bedtime?.Workdays, bedtime?.OffDays }
            .OfType<SyncSmartLevel>().Where(level => level is { AverageSleptMinutes: not null, Nights: > 0 }).ToList();
        if (measured.Count == 0)
        {
            return null;
        }

        int nights = measured.Sum(level => level.Nights);
        double minutes = measured.Sum(level => level.AverageSleptMinutes!.Value * (double)level.Nights) / nights;
        return (TimeSpan.FromMinutes(Math.Round(minutes)), nights, measured.Sum(level => level.MadeThisWeek), measured.Sum(level => level.AskedThisWeek));
    }

    private static TimeSpan Time(IReadOnlyList<DriftStretch> visits)
    {
        return visits.Aggregate(TimeSpan.Zero, (sum, visit) => sum + (visit.End - visit.Start));
    }

    private static string Down(int minutes)
    {
        return minutes < 60 ? $"{minutes} min" : StatsFormat.Duration(TimeSpan.FromMinutes(minutes));
    }

    private static string Plural(int count, string noun, string? many = null) => count == 1 ? noun : many ?? noun + "s";
}
