using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Reads a typed clock time ("6", "6:30", "6:30pm", "6.30 am", "18:30", "630") and turns it into the next
/// moment that time occurs. Without am/pm, a 12-hour time picks whichever of am/pm comes first, so at
/// 5:20 PM "6" means 6:00 PM and at 11 PM "1" means 1:00 AM tomorrow.
/// </summary>
public static partial class ClockTimeParser
{
    public static TimeSpan MaxAhead { get; } = TimeSpan.FromHours(12);

    /// <param name="now">The current local time.</param>
    /// <param name="target">The next local time matching the text, at most <see cref="MaxAhead"/> away.</param>
    public static bool TryParseUpcoming(
        string? text, DateTime now, out DateTime target, [NotNullWhen(false)] out string? error)
    {
        target = default;
        Match match = TimePattern().Match((text ?? string.Empty).Trim());
        if (!match.Success)
        {
            error = "Type a time like 6, 6:30, 6:30pm or 18:30.";
            return false;
        }

        int hour = int.Parse(match.Groups["hour"].Value);
        int minute = match.Groups["minute"].Success ? int.Parse(match.Groups["minute"].Value) : 0;
        string meridiem = match.Groups["meridiem"].Value.ToLowerInvariant();

        if (minute > 59 || hour > 23 || (meridiem.Length > 0 && (hour < 1 || hour > 12)))
        {
            error = "That isn't a valid time.";
            return false;
        }

        IEnumerable<int> candidateHours = meridiem switch
        {
            "am" or "a" => new[] { hour % 12 },
            "pm" or "p" => new[] { (hour % 12) + 12 },
            // 13–23 and 0 can only mean one thing; 1–12 may be morning or evening.
            _ when hour == 0 || hour > 12 => new[] { hour },
            _ => new[] { hour % 12, (hour % 12) + 12 },
        };

        DateTime best = candidateHours
            .Select(candidate => NextOccurrence(now, candidate, minute))
            .Min();

        if (best - now > MaxAhead)
        {
            error = $"{best:t} is more than 12 hours away.";
            return false;
        }

        target = best;
        error = null;
        return true;
    }

    private static DateTime NextOccurrence(DateTime now, int hour, int minute)
    {
        DateTime today = now.Date.AddHours(hour).AddMinutes(minute);
        return today > now ? today : today.AddDays(1);
    }

    // "6", "06", "6:30", "6.30", "630", "1830", each optionally followed by am/pm (a/p, with or without a space).
    [GeneratedRegex(@"^(?<hour>\d{1,2})(?:[:.]?(?<minute>\d{2}))?\s*(?<meridiem>am|pm|a|p)?\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex TimePattern();
}
