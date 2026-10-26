namespace AccountabiliBuddy.Core.Domain;

/// <summary>The bedtime reminders (ADR 38): on or off, and the list. Part of the shared sleep settings.</summary>
public sealed record BedtimeReminderSettings(bool On, IReadOnlyList<BedtimeReminder> List)
{
    /// <summary>45 and 15 minutes before (notifications on the phone and the watch), and an alarm at bedtime on the phone.</summary>
    public static BedtimeReminderSettings Default { get; } = new(true, new[]
    {
        new BedtimeReminder(45, false, new[] { BedtimeReminder.Phone, BedtimeReminder.Watch }),
        new BedtimeReminder(15, false, new[] { BedtimeReminder.Phone, BedtimeReminder.Watch }),
        new BedtimeReminder(0, true, new[] { BedtimeReminder.Phone }),
    });
}
