using System.Globalization;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The words for a focus session that runs on another device of the group (ADR 85, rules R2 and R3): the same text everywhere the PC
/// shows it (title bar, Today's start card, the quick-session popup, Goals, a refused start request). Times are 12-hour with AM/PM.
/// </summary>
public static class SessionElsewhereText
{
    /// <summary>"32 min left", "1 h 05 min left", "no set end"; "· paused" after it while the timer is paused.</summary>
    public static string Detail(GroupSessionLive live, DateTimeOffset now)
    {
        string core = live.NoSetEnd || live.EndsAt is not DateTimeOffset end ? "no set end" : $"{Minutes(end - now)} left";
        return live.Paused ? $"{core} · paused" : core;
    }

    /// <summary>Start's replacement on Today and in the popup: "Running on Pixel · 32 min left".</summary>
    public static string Running(GroupSessionLive live, DateTimeOffset now) => $"Running on {ShortName(live.Name)} · {Detail(live, now)}";

    /// <summary>The title bar's text: "Session on Pixel · 32 min left".</summary>
    public static string OnDevice(GroupSessionLive live, DateTimeOffset now) => $"Session on {ShortName(live.Name)} · {Detail(live, now)}";

    /// <summary>The title bar's tooltip: only that device ends its own session (R6).</summary>
    public static string OnlyCanEnd(GroupSessionLive live) => $"Only {live.Name} can end it";

    /// <summary>The answer to a start request (R5) and to a start the PC itself refuses.</summary>
    public static string Refusal(GroupSessionLive live) => $"A session is running on {live.Name}.";

    /// <summary>R3: Start is back, with this muted line, when a phone's session went quiet: "Pixel's session last heard from 2:05 PM".</summary>
    public static string LastHeard(GroupSessionStale stale, TimeZoneInfo zone)
    {
        string time = TimeZoneInfo.ConvertTime(stale.LastHeardAt, zone).ToString("h:mm tt", CultureInfo.InvariantCulture);
        return $"{stale.Name}'s session last heard from {time}";
    }

    // A long device name never makes the line wrap (Today's card keeps its height): 14 characters, then an ellipsis.
    private const int NameLimit = 14;

    private static string ShortName(string name) => name.Length <= NameLimit ? name : name[..(NameLimit - 1)].TrimEnd() + "…";

    // A countdown rounds up (never "0 min left" while time is left): "32 min", or "1 h 05 min" from an hour.
    private static string Minutes(TimeSpan span)
    {
        int minutes = (int)Math.Ceiling(Math.Max(0, span.TotalMinutes));
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00} min";
    }
}
