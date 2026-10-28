using System.Globalization;
using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Builds the right column's day: the 12 AM to 12 AM strip, the Deep work and Phone tiles and the day's list.</summary>
internal static class StatsDayBuilder
{
    public static StatsDayPanel Build(StatsData data, StatsLookups lookups, DateOnly day)
    {
        DateTime start = day.ToDateTime(TimeOnly.MinValue);
        var window = new LocalWindow(start, start.AddDays(1));
        var spans = new List<DayStripSpan>();
        foreach (LocalWindow sleep in lookups.SleepOn(day))
        {
            Add(spans, window, sleep, DayStripKind.Sleep, "Planned sleep");
        }

        foreach (LocalWindow focus in data.Windows)
        {
            Add(spans, window, focus, DayStripKind.Focus, "Focus");
        }

        IReadOnlyList<DriftStretch> visits = data.DriftBetween(day, day);
        foreach (DriftStretch visit in visits)
        {
            Add(spans, window, Local(data, visit.Start, visit.End), DayStripKind.Drift, "Drift");
        }

        TimeSpan focused = data.DayOf(day)?.Focused ?? TimeSpan.Zero;
        bool met = focused >= data.Goal;
        string note = met ? "goal met" : day == data.Today ? $"{StatsFormat.Duration(data.Goal - focused)} to go" : $"goal {StatsFormat.Duration(data.Goal)}";
        IReadOnlyList<Services.Sync.ReceivedSession> others = data.Received.Where(session => data.DateOf(session.Start) == day).ToList();

        var rows = new List<StatsDayRow>();
        foreach (SessionRecord record in data.Records.Where(record => data.DateOf(record.StartedAt) == day
                     && record.Mode != SessionMode.Scheduled && !lookups.IsOmitted(record.StartedAt)))
        {
            string title = record.Mode == SessionMode.Added ? "Added by hand" : lookups.ProfileName(record.ProfileId);
            rows.Add(new StatsDayRow(record.StartedAt, Time(data, record.StartedAt), title,
                record.EndedAt is { } ended ? StatsFormat.Duration(ended - record.StartedAt) : "running", IsDrift: false));
        }

        foreach (Services.Sync.ReceivedSession session in others)
        {
            rows.Add(new StatsDayRow(session.Start, Time(data, session.Start), session.Added ? "Added by hand" : session.Profile ?? "Focus session",
                StatsFormat.Duration(session.End - session.Start), IsDrift: false));
        }

        foreach (DriftStretch visit in visits)
        {
            int minutes = Math.Max(1, (int)Math.Round((visit.End - visit.Start).TotalMinutes));
            rows.Add(new StatsDayRow(visit.Start, Time(data, visit.Start), $"{lookups.SiteName(visit.Site)}, {minutes} min", "off track", IsDrift: true));
        }

        IReadOnlyList<Core.Sync.SyncPickup> pickups = data.PhoneCounts ? data.PickupsBetween(day, day) : [];
        return new StatsDayPanel(
            spans,
            StatsFormat.HoursMinutes(focused),
            note,
            met,
            pickups.Count == 0 ? null : pickups.Sum(pickup => pickup.Count).ToString(CultureInfo.CurrentCulture),
            "pickups in sessions",
            rows.OrderBy(row => row.At).ToList());
    }

    private static void Add(List<DayStripSpan> spans, LocalWindow day, LocalWindow window, DayStripKind kind, string name)
    {
        if (DayTimeline.Clip(window, day) is not { } shown)
        {
            return;
        }

        string tip = $"{name} {shown.Start.ToString("t", CultureInfo.CurrentCulture)} – {shown.End.ToString("t", CultureInfo.CurrentCulture)}";
        spans.Add(new DayStripSpan((shown.Start - day.Start).TotalDays, (shown.End - day.Start).TotalDays, kind, tip));
    }

    private static LocalWindow Local(StatsData data, DateTimeOffset from, DateTimeOffset to)
    {
        return new LocalWindow(TimeZoneInfo.ConvertTime(from, data.Zone).DateTime, TimeZoneInfo.ConvertTime(to, data.Zone).DateTime);
    }

    private static string Time(StatsData data, DateTimeOffset at)
    {
        return TimeZoneInfo.ConvertTime(at, data.Zone).ToString("t", CultureInfo.CurrentCulture);
    }
}
