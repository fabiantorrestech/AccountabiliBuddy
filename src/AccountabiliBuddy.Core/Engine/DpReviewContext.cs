using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>What the Setup DP Assistant's review needs besides the plan.</summary>
/// <param name="GoalMinutes">The daily deep work goal.</param>
/// <param name="SleepOn">Sleep mode is on (bedtime reminders, the sleep line, alarms).</param>
/// <param name="Reminders">The bedtime reminders.</param>
/// <param name="WakeSource">Where a Sleep part woken by the alarm gets its wake time, e.g. "your phone's alarm".</param>
/// <param name="WorkHoursGuardOn">This PC guards work hours (the goal lifts the guard once met).</param>
/// <param name="SmartBedtime">The smart bedtime is on for nights before workdays or other days.</param>
/// <param name="NightFallback">The night before a date when the plan has no Sleep part for it (the sleep settings'); null = none.</param>
public sealed record DpReviewContext(
    int GoalMinutes,
    bool SleepOn,
    BedtimeReminderSettings Reminders,
    string WakeSource,
    bool WorkHoursGuardOn,
    bool SmartBedtime,
    Func<DateOnly, SleepTimes?>? NightFallback);
