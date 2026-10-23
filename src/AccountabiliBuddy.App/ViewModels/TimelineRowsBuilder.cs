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

    /// <summary>Slate: work time with no focus session (not a day plan category; worked out from the plan and sessions).</summary>
    public const string NotInSessionColor = "#64748B";

    /// <summary>White: a guided session's ticked task on the Focus row.</summary>
    public const string TaskMarkColor = "#FFFFFF";

    /// <summary>Time marked distracted in History: drift's red, over the session.</summary>
    public const string MarkedDistractedColor = "#EF4444";

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
                TimelineRow.Focus => new DayBarRow(TimelineRow.Focus, $"Focus {Duration(day.Focused)}", [.. GapSpans(day), .. SessionSpans(day)],
                    [.. Items(day.Drift, day.Range), .. Items(day.TaskMarks ?? [], day.Range, allowEmpty: true)]),
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
        List<LocalWindow> nights = day.Parts.Where(part => part.Category.Behavior == CategoryBehavior.Sleep).Select(part => part.Window).ToList();
        foreach (PlannedPart part in day.Parts)
        {
            if (DayTimeline.Clip(part.Window, day.Range) is not LocalWindow shown)
            {
                continue;
            }

            string tip = $"{part.Name} · {Times(part.Window)}";
            if (part.Part.IsFlexible)
            {
                // A flexible break's window (schema v8): see-through over the work around it until it's taken.
                spans.Add(ToSpan(shown, day.Range, $"{part.Name} window · {Times(part.Window)} ({Duration(TimeSpan.FromMinutes(part.Part.FlexMinutes!.Value))}, start any time)")
                    with { Color = part.Category.Color, IsOverlap = true });
                continue;
            }

            if (part.Category.Behavior != CategoryBehavior.PreWork || nights.Count == 0)
            {
                spans.Add(ToSpan(shown, day.Range, tip) with { Color = part.Category.Color, Label = part.Name });
                continue;
            }

            // Pre-Work over Sleep (ADR 31): the part that overlaps the night is drawn see-through, the rest as usual.
            foreach (LocalWindow awake in DayTimeline.Subtract([shown], nights))
            {
                spans.Add(ToSpan(awake, day.Range, tip) with { Color = part.Category.Color, Label = part.Name });
            }

            foreach (LocalWindow over in DayTimeline.Subtract([shown], DayTimeline.Subtract([shown], nights)))
            {
                spans.Add(ToSpan(over, day.Range, $"{tip} (over Sleep: guarded if you're up early)") with { Color = part.Category.Color, IsOverlap = true });
            }
        }

        // A flexible break as taken (lunch started): solid, on top.
        foreach (TimelineItem taken in day.Breaks ?? [])
        {
            if (DayTimeline.Clip(taken.Window, day.Range) is LocalWindow shown)
            {
                spans.Add(ToSpan(shown, day.Range, taken.Tip) with { Color = taken.Color, Label = "Lunch" });
            }
        }

        return spans;
    }

    // Work time with no session: under the sessions, slate, and still empty for "add a session" clicks.
    private static List<DayBarSpan> GapSpans(TimelineDay day)
    {
        var spans = new List<DayBarSpan>();
        foreach (LocalWindow gap in day.Unfocused ?? [])
        {
            if (DayTimeline.Clip(gap, day.Range) is LocalWindow shown)
            {
                spans.Add(ToSpan(shown, day.Range, $"Not in a session · {Times(gap)} ({Duration(gap.End - gap.Start)})")
                    with { Color = NotInSessionColor, IsGap = true });
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
                spans.Add(ToSpan(shown, day.Range, session.Tip) with { ProfileId = session.ProfileId, Hatched = session.Added, Color = session.Color });

                // Stepped away: hatched slate over the session, after it so it draws on top.
                foreach (LocalWindow away in session.Away ?? [])
                {
                    if (DayTimeline.Clip(away, shown) is LocalWindow awayShown)
                    {
                        spans.Add(ToSpan(awayShown, day.Range, $"Away · {Times(away)} ({Duration(away.End - away.Start)})")
                            with { Color = NotInSessionColor, Hatched = true, IsAway = true });
                    }
                }

                // Marked distracted in History: red over the session, like drift.
                foreach (LocalWindow distracted in session.Distracted ?? [])
                {
                    if (DayTimeline.Clip(distracted, shown) is LocalWindow distractedShown)
                    {
                        spans.Add(ToSpan(distractedShown, day.Range, $"Marked distracted · {Times(distracted)} ({Duration(distracted.End - distracted.Start)})")
                            with { Color = MarkedDistractedColor });
                    }
                }
            }
        }

        return spans;
    }

    // allowEmpty: a mark at one moment (a ticked task) has no length; it's drawn at least the mark's width.
    private static List<DayBarSpan> Items(IReadOnlyList<TimelineItem> items, LocalWindow range, bool allowEmpty = false)
    {
        var spans = new List<DayBarSpan>();
        foreach (TimelineItem item in items)
        {
            if (DayTimeline.Clip(item.Window, range) is LocalWindow shown)
            {
                spans.Add(ToSpan(shown, range, item.Tip) with { Color = item.Color, IsSliver = item.AllDay });
            }
            else if (allowEmpty && item.Window.Start >= range.Start && item.Window.Start <= range.End)
            {
                spans.Add(ToSpan(item.Window, range, item.Tip) with { Color = item.Color, IsSliver = item.AllDay });
            }
        }

        // All-day slivers first, so timed events draw over them.
        return spans.OrderByDescending(span => span.IsSliver).ToList();
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
