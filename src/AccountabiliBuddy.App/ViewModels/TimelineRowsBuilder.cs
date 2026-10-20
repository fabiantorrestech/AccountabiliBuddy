using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Turns one day's data into the timeline's rows (Calendar, Plan, Focus, Blockers), in the order and with the rows
/// chosen in Settings, each stretch clipped to the bar and placed on its 0–1 width.
/// </summary>
public static class TimelineRowsBuilder
{
    /// <summary>Amber, like a warning sign: what the blockers block.</summary>
    public const string BlockerColor = "#F59E0B";

    /// <param name="rows">Every row in order with whether it shows (<see cref="DayBarSettings.RowsInOrder"/>).</param>
    public static IReadOnlyList<DayBarRow> Build(TimelineDay day, IReadOnlyList<TimelineRowChoice> rows)
    {
        var built = new List<DayBarRow>();
        foreach (TimelineRowChoice choice in rows.Where(choice => choice.Shown))
        {
            DayBarRow? row = choice.Row switch
            {
                TimelineRow.Calendar => new DayBarRow(TimelineRow.Calendar, "Calendar", Items(day.Events, day.Range), Array.Empty<DayBarSpan>()),
                TimelineRow.Plan => new DayBarRow(TimelineRow.Plan, "Plan", PlanSpans(day), Array.Empty<DayBarSpan>()),
                TimelineRow.Focus => new DayBarRow(TimelineRow.Focus, $"Focus {Duration(day.Focused)}", SessionSpans(day), Items(day.Drift, day.Range)),
                TimelineRow.Blockers when day.Blockers is not null =>
                    new DayBarRow(TimelineRow.Blockers, "Cold Turkey", BlockerSpans(day.Blockers, day.Range), Array.Empty<DayBarSpan>()),
                _ => null,
            };
            if (row is not null)
            {
                built.Add(row);
            }
        }

        return built;
    }

    /// <summary>
    /// The day plan's parts around <paramref name="span"/> with their real times: a day either side, so a night that
    /// starts the evening before or runs into the next morning keeps its whole window for the tip (the rows clip it).
    /// </summary>
    public static IReadOnlyList<PlannedPart> PartsAround(DayPlan plan, LocalWindow span)
    {
        return plan.PartsIn(new LocalWindow(span.Start.AddDays(-1), span.End.AddDays(1)));
    }

    /// <summary>The drift marks that count: inside a session drawn on the Focus row, inside the bar.</summary>
    public static IReadOnlyList<TimelineItem> DriftOnBar(IReadOnlyList<TimelineItem> marks, IReadOnlyList<TimelineSession> sessions, LocalWindow range)
    {
        return marks.Where(mark => sessions.Any(session =>
                DayTimeline.Clip(mark.Window, session.Window) is LocalWindow inSession && DayTimeline.Clip(inSession, range) is not null))
            .ToList();
    }

    /// <summary>E.g. "r/manybaggers · 11:35 AM – 11:41 AM (6m)"; under a minute reads "(&lt;1m)".</summary>
    public static string DriftTip(string site, LocalWindow window)
    {
        TimeSpan length = window.End - window.Start;
        string duration = length < TimeSpan.FromMinutes(1) ? "<1m" : Duration(length);
        return $"{site} · {Times(window)} ({duration})";
    }

    /// <summary>E.g. "2h 55m" or "40m".</summary>
    public static string Duration(TimeSpan duration)
    {
        int minutes = (int)Math.Round(duration.TotalMinutes);
        return minutes >= 60 ? $"{minutes / 60}h {minutes % 60:00}m" : $"{minutes}m";
    }

    private static List<DayBarSpan> PlanSpans(TimelineDay day)
    {
        var spans = new List<DayBarSpan>();
        foreach (PlannedPart part in day.Parts)
        {
            if (DayTimeline.Clip(part.Window, day.Range) is LocalWindow shown)
            {
                spans.Add(ToSpan(shown, day.Range, $"{part.Name} · {Times(part.Window)}") with { Color = part.Category.Color, Label = part.Name });
            }
        }

        return spans;
    }

    private static List<DayBarSpan> SessionSpans(TimelineDay day)
    {
        var spans = new List<DayBarSpan>();
        foreach (TimelineSession session in day.Sessions)
        {
            if (DayTimeline.Clip(session.Window, day.Range) is LocalWindow shown)
            {
                spans.Add(ToSpan(shown, day.Range, session.Tip) with { ProfileId = session.ProfileId });
            }
        }

        return spans;
    }

    private static List<DayBarSpan> Items(IReadOnlyList<TimelineItem> items, LocalWindow range)
    {
        var spans = new List<DayBarSpan>();
        foreach (TimelineItem item in items)
        {
            if (DayTimeline.Clip(item.Window, range) is LocalWindow shown)
            {
                spans.Add(ToSpan(shown, range, item.Tip) with { Color = item.Color });
            }
        }

        return spans;
    }

    private static List<DayBarSpan> BlockerSpans(IReadOnlyList<BlockerWindow> blockers, LocalWindow range)
    {
        var spans = new List<DayBarSpan>();
        foreach (BlockerWindow blocker in blockers)
        {
            if (DayTimeline.Clip(blocker.Window, range) is LocalWindow shown)
            {
                string when = blocker.AlwaysOn ? "always on" : Times(blocker.Window);
                spans.Add(ToSpan(shown, range, $"{blocker.Name} · {when}") with { Color = BlockerColor });
            }
        }

        return spans;
    }

    private static DayBarSpan ToSpan(LocalWindow window, LocalWindow range, string tip)
    {
        return new DayBarSpan(Fraction(window.Start, range), Fraction(window.End, range), tip);
    }

    private static double Fraction(DateTime time, LocalWindow range)
    {
        double length = (range.End - range.Start).TotalMinutes;
        return length <= 0 ? 0 : (time - range.Start).TotalMinutes / length;
    }

    private static string Times(LocalWindow window)
    {
        return $"{window.Start:t} – {window.End:t}";
    }
}
