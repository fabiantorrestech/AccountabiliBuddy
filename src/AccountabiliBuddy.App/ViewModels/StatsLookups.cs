using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the Stats builders ask the app: names and plan rules that live outside the history.</summary>
/// <param name="ProfileName">A profile's name, or what to call a session of a deleted one.</param>
/// <param name="SiteName">A site or app as the user renamed it in History.</param>
/// <param name="IsOmitted">The session was left out of the totals.</param>
/// <param name="IsWorkday">Whether a date is a workday in the day plan (off days never count against the user).</param>
/// <param name="SleepOn">The planned sleep touching a date, as local windows; empty with sleep mode off.</param>
internal sealed record StatsLookups(
    Func<Guid?, string> ProfileName,
    Func<string, string> SiteName,
    Func<DateTimeOffset, bool> IsOmitted,
    Func<DateOnly, bool> IsWorkday,
    Func<DateOnly, IReadOnlyList<LocalWindow>> SleepOn);
