using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// When tonight's bedtime reminders fire (ADR 38): each at bedtime minus its minutes, in firing order, labelled
/// "n/N". Worked out again whenever bedtime moves (an alarm, the smart bedtime, a one-night change).
/// </summary>
public static class BedtimeReminderTimes
{
    /// <summary>One reminder tonight: when, which (1-based, of how many), and the reminder.</summary>
    public sealed record Due(DateTime At, int Number, int Count, BedtimeReminder Reminder);

    /// <summary>A reminder this many minutes late still fires (the app was asleep for a moment).</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    public static IReadOnlyList<Due> For(DateTime bedtime, BedtimeReminderSettings settings)
    {
        if (!settings.On)
        {
            return Array.Empty<Due>();
        }

        List<BedtimeReminder> ordered = settings.List.Where(r => r.MinutesBefore >= 0).OrderByDescending(r => r.MinutesBefore).ToList();
        return ordered.Select((r, i) => new Due(bedtime.AddMinutes(-r.MinutesBefore), i + 1, ordered.Count, r)).ToList();
    }

    /// <summary>The night's id for dismissals: its bedtime's date ("2026-10-25").</summary>
    public static string NightId(DateTime bedtime) => DateOnly.FromDateTime(bedtime.Hour < 12 ? bedtime.AddDays(-1) : bedtime).ToString("yyyy-MM-dd");
}
