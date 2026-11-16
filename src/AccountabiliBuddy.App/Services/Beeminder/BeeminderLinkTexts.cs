using System.Globalization;

namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>
/// The words for a goal's Beeminder read link (R8.2): safe days, the chip and the road as a pace line. Pure text over the cached
/// <see cref="BeeminderGoal"/>; never reads or stores a datapoint or its comment.
/// </summary>
public static class BeeminderLinkTexts
{
    /// <summary>"Due today" (no buffer, or the deadline falls today), "1 safe day" or "N safe days".</summary>
    public static string SafeDays(BeeminderGoal goal, DateTimeOffset now, TimeZoneInfo zone)
    {
        return IsDueToday(goal, now, zone) ? "Due today" : goal.SafeDays == 1 ? "1 safe day" : $"{goal.SafeDays} safe days";
    }

    /// <summary>No buffer left, or the deadline falls today (the safe days are from the last answer).</summary>
    public static bool IsDueToday(BeeminderGoal goal, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime due = TimeZoneInfo.ConvertTime(goal.DueAt, zone).DateTime;
        DateTime today = TimeZoneInfo.ConvertTime(now, zone).Date;
        return goal.SafeDays <= 0 || due.Date <= today;
    }

    /// <summary>
    /// The chip for a link whose key (the slug, or the title of a link made before ADR 84) is <paramref name="key"/>. A goal missing from the
    /// cached list (no token on this PC, or not listed) still gets "Beeminder · key"; an ambiguous title gets no safe days either.
    /// </summary>
    public static BeeminderLinkInfo For(string key, IReadOnlyList<BeeminderGoal> cached, DateTimeOffset now, TimeZoneInfo zone)
    {
        (BeeminderGoal? goal, _) = BeeminderDatapointsService.FindGoal(cached, key);
        if (goal is null)
        {
            return new BeeminderLinkInfo($"Beeminder · {key}", "Beeminder", null, false);
        }

        string name = goal.Slug.Length > 0 ? goal.Slug : key;
        string safe = SafeDays(goal, now, zone).ToLowerInvariant();
        return new BeeminderLinkInfo($"Beeminder · {name} · {safe}", $"Beeminder · {safe}", RoadLine(goal), IsDueToday(goal, now, zone));
    }

    /// <summary>
    /// The road as a pace line: "Road · 3 pages a week", then what's needed ("· +2 within 1 day") when Beeminder says. Null when the cached
    /// answer has no rate (the line is left out rather than guessed).
    /// </summary>
    public static string? RoadLine(BeeminderGoal goal)
    {
        if (goal.Rate is not double rate || UnitWord(goal.RateUnit) is not string perUnit)
        {
            return null;
        }

        // A do-less goal's road runs downhill: read it by its size. A rate too small to show reads "under 0.01".
        double size = Math.Abs(rate);
        string amount = size > 0 && size < 0.005 ? "under 0.01" : Math.Round(size, 2).ToString("0.##", CultureInfo.InvariantCulture);
        string units = goal.Units.Length > 0 ? " " + goal.Units : string.Empty;
        string article = perUnit == "hour" ? "an" : "a";
        string line = $"Road ends at {amount}{units} {article} {perUnit}";
        return goal.Needed.Length > 0 ? $"{line} · {goal.Needed}" : line;
    }

    private static string? UnitWord(string runits) => runits switch
    {
        "d" => "day",
        "w" => "week",
        "m" => "month",
        "y" => "year",
        "h" => "hour",
        _ => null,
    };
}
