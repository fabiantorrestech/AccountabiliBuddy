namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Which device can measure each source until the round 2 ADRs are accepted (plan §3b 2, 5, 7; brief §6.1). The UI greys a
/// <see cref="GoalSourceAvailability.ComingSoon"/> source and labels the others "Measured on your PC / phone" where they can't
/// be counted.
/// </summary>
public static class GoalSources
{
    public static GoalSourceAvailability AvailabilityOf(GoalSourceType type)
    {
        return type switch
        {
            // Every device counts focus from the sealed session data it already receives.
            GoalSourceType.FocusAll or GoalSourceType.FocusLabel or GoalSourceType.Hand => GoalSourceAvailability.Everywhere,

            // Daily steps already sync (phone reads, PC shows).
            GoalSourceType.HealthSteps => GoalSourceAvailability.Everywhere,

            // Plan §3b 5: tags count on the PC from its local data, "Measured on your PC" on the phone, until ADR 79.
            GoalSourceType.FocusTag => GoalSourceAvailability.PcOnly,

            // Plan §3b 7 (calendar, PC only, never in focus totals); ADR 43 (the PC reads Beeminder).
            GoalSourceType.CalendarCategory or GoalSourceType.Beeminder => GoalSourceAvailability.PcOnly,

            // Health Connect sleep and phone usage stay on the phone (hard rule "The phone's Stats tab") until ADR 79.
            GoalSourceType.HealthSleep or GoalSourceType.ScreenTime => GoalSourceAvailability.PhoneOnly,

            // Plan §3b 2 (task goal link, ADR 79) and ADR 81 (READ_WEIGHT).
            GoalSourceType.GoalField or GoalSourceType.MilestoneTasks or GoalSourceType.HealthWeight => GoalSourceAvailability.ComingSoon,
            _ => GoalSourceAvailability.ComingSoon,
        };
    }
}
