using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.App.Services.Search;

/// <summary>
/// The user's own things as search entries: profiles and their rules (Settings > Guards), the day plan (categories,
/// parts, one-off days) and calendar events (Plan), and the apps and sites in History (Stats). Built from what the app holds when searching, so it's never stale.
/// </summary>
public static class DataSearchSource
{
    public const int DefaultCap = 200;

    /// <param name="usage">Apps and sites from History (readable columns only), e.g. the last 90 days.</param>
    /// <param name="shownNames">History's display names for apps and sites, found under either name.</param>
    /// <param name="today">Picks a repeating event's next date; defaults to the computer's today.</param>
    public static IReadOnlyList<SearchEntry> Entries(
        ProfileDocument document,
        IReadOnlyList<CalendarEvent> events,
        IReadOnlyList<AppSiteUsage> usage,
        IReadOnlyDictionary<string, string>? shownNames,
        DateOnly? today = null)
    {
        var entries = new List<SearchEntry>();
        AddProfiles(entries, document.Profiles);
        AddDayPlan(entries, document.DayPlan);
        AddEvents(entries, events, today ?? DateOnly.FromDateTime(DateTime.Now));
        AddHistory(entries, usage, shownNames);
        return entries;
    }

    /// <summary>The entries whose text contains the query (ignoring case), in their order, at most <paramref name="cap"/>.</summary>
    public static IReadOnlyList<SearchEntry> Filter(IEnumerable<SearchEntry> entries, string query, int cap = DefaultCap)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<SearchEntry>();
        }

        return entries.Where(entry => TextMatcher.Find(entry.Text, query).Count > 0).Take(cap).ToList();
    }

    private static void AddProfiles(List<SearchEntry> entries, IReadOnlyList<FocusProfile> profiles)
    {
        foreach (FocusProfile profile in profiles)
        {
            var target = new SearchTarget(SearchTabs.Settings, SettingsGroup: SearchTabs.GuardsRow, ProfileId: profile.Id);
            entries.Add(new SearchEntry(profile.Name, "Settings › Guards › Profiles", SearchKinds.Profiles, target));
            foreach (Rule rule in profile.Rules)
            {
                string verdict = rule.Verdict == RuleVerdict.Block ? "block" : "allow";
                entries.Add(new SearchEntry($"{rule.Pattern} ({verdict} {TargetName(rule.Target)})", $"Settings › Guards › {profile.Name}", SearchKinds.Profiles, target));
            }
        }
    }

    private static void AddDayPlan(List<SearchEntry> entries, DayPlan plan)
    {
        var planTab = new SearchTarget(SearchTabs.Plan);
        foreach (DayCategory category in plan.Categories)
        {
            entries.Add(new SearchEntry($"{category.Name} ({category.Behavior})", "Plan › Categories", SearchKinds.Plan, planTab));
        }

        for (int index = 0; index < plan.Templates.Count; index++)
        {
            PlanTemplate template = plan.Templates[index];
            entries.Add(new SearchEntry(template.Name, "Plan › Day types", SearchKinds.Plan, planTab with { PlanTemplate = index }));
            AddTemplate(entries, plan, template.Template, $"Plan › {template.Name}", planTab with { PlanTemplate = index });
        }

        foreach (OneOffDay day in plan.OneOffDays)
        {
            string date = day.Date.ToString("ddd M/d/yyyy");
            var target = new SearchTarget(SearchTabs.Plan, Date: day.Date);
            entries.Add(new SearchEntry(day.Note is null ? $"One-off day {date}" : $"{day.Note} · one-off day {date}", "Plan › Change a day › One-off days", SearchKinds.Plan, target));
            AddTemplate(entries, plan, day.Template, $"Plan › Change a day › {date}", target);
        }
    }

    private static void AddTemplate(List<SearchEntry> entries, DayPlan plan, DayTemplate template, string path, SearchTarget target)
    {
        foreach (PlanPart part in template.Parts)
        {
            string name = part.Label ?? plan.Category(part.CategoryId)?.Name ?? "Part";
            string times = $"{part.Start.ToString("t")} – {part.End.ToString("t")}";
            entries.Add(new SearchEntry($"{name} {times}", path, SearchKinds.Plan, target));
        }
    }

    /// <summary>One row per event title in a calendar: a repeating event (a daily lunch) shows once, on its next date.</summary>
    private static void AddEvents(List<SearchEntry> entries, IReadOnlyList<CalendarEvent> events, DateOnly today)
    {
        DateTime startOfToday = today.ToDateTime(TimeOnly.MinValue);
        IEnumerable<CalendarEvent> shown = events
            .GroupBy(calendarEvent => (calendarEvent.CalendarId, calendarEvent.Title))
            .Select(series => series.Where(occurrence => occurrence.LocalEnd > startOfToday).OrderBy(occurrence => occurrence.LocalStart).FirstOrDefault()
                              ?? series.OrderByDescending(occurrence => occurrence.LocalStart).First());
        foreach (CalendarEvent calendarEvent in shown.OrderBy(occurrence => occurrence.LocalStart))
        {
            DateOnly date = DateOnly.FromDateTime(calendarEvent.LocalStart);
            string when = calendarEvent.IsAllDay ? date.ToString("ddd M/d") : $"{date:ddd M/d}, {calendarEvent.LocalStart:t}";
            entries.Add(new SearchEntry($"{calendarEvent.Title} · {when}", "Plan › Change a day › Events", SearchKinds.Calendar,
                new SearchTarget(SearchTabs.Plan, Date: date)));
        }
    }

    private static void AddHistory(List<SearchEntry> entries, IReadOnlyList<AppSiteUsage> usage, IReadOnlyDictionary<string, string>? shownNames)
    {
        var target = new SearchTarget(SearchTabs.Stats);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AppSiteUsage item in usage)
        {
            string name = item.Site is null ? item.App : item.Section is null ? item.Site : $"{item.Site} {item.Section}";
            if (!seen.Add(name))
            {
                continue;
            }

            string key = item.Site ?? item.App;
            string text = shownNames is not null && shownNames.TryGetValue(key, out string? shown) && shown != key ? $"{shown} ({name})" : name;
            entries.Add(new SearchEntry(text, "Stats › History › Most used", SearchKinds.History, target));
        }
    }

    private static string TargetName(MatchTarget target)
    {
        return target switch
        {
            MatchTarget.ProcessName => "app",
            MatchTarget.WindowTitle => "window title",
            MatchTarget.Url => "site",
            _ => "screen text",
        };
    }
}
