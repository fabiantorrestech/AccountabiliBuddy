package com.accountabilibuddy.shared.goals

import java.util.UUID

/**
 * Which device can measure each source until the round 2 ADRs are accepted (plan §3b 2, 5, 7; brief §6.1), and which rules
 * count for a goal; the PC's `GoalSources`. The phone greys a [GoalSourceAvailability.ComingSoon] source and labels a
 * [GoalSourceAvailability.PcOnly] one "Measured on your PC" ([GoalPhoneRules]).
 */
object GoalSources {
    /** Deep work's one source: all counted focus, switched on. The same id on every device. */
    val DEEP_WORK_RULE: GoalSourceRule = GoalSourceRule(UUID.fromString("d33b0a11-6f0c-4e2a-9d51-0000000000f1"), GoalSourceType.FocusAll)

    /**
     * The rules that count for [goal]: Deep work ([Goal.isDeepWork]) counts all focus and nothing else whatever its stored record
     * says (plan §3b 6: no hand, calendar or steps sources); every other goal its own rules.
     */
    fun countedFor(goal: Goal): List<GoalSourceRule> = if (goal.isDeepWork) listOf(DEEP_WORK_RULE) else goal.sources

    /**
     * Whether the editor may add a source of [type] to [goal]: Deep work takes only all focus (locked); every other goal takes
     * any source but all focus (Deep work's own) and the "Coming soon" ones.
     */
    fun allowedFor(goal: Goal, type: GoalSourceType): Boolean {
        if (goal.isDeepWork) {
            return type == GoalSourceType.FocusAll
        }

        return type != GoalSourceType.FocusAll && availabilityOf(type) != GoalSourceAvailability.ComingSoon
    }

    fun availabilityOf(type: GoalSourceType): GoalSourceAvailability = when (type) {
        // Every device counts focus from the sealed session data it already receives.
        GoalSourceType.FocusAll, GoalSourceType.FocusLabel, GoalSourceType.Hand -> GoalSourceAvailability.Everywhere

        // Daily steps already sync (phone reads, PC shows).
        GoalSourceType.HealthSteps -> GoalSourceAvailability.Everywhere

        // Plan §3b 5: tags count on the PC from its local data, "Measured on your PC" on the phone, until ADR 79.
        GoalSourceType.FocusTag -> GoalSourceAvailability.PcOnly

        // Plan §3b 7 (calendar, PC only, never in focus totals); ADR 43 (the PC reads Beeminder).
        GoalSourceType.CalendarCategory, GoalSourceType.Beeminder -> GoalSourceAvailability.PcOnly

        // Health Connect sleep stays on the phone until ADR 79.
        GoalSourceType.HealthSleep -> GoalSourceAvailability.PhoneOnly

        // Screen time v2: each device counts its own usage (the PC from its activity history, the phone from Usage access).
        GoalSourceType.ScreenTime -> GoalSourceAvailability.Everywhere

        // Plan §3b 2 (task goal link, ADR 79) and ADR 81 (READ_WEIGHT).
        GoalSourceType.GoalField, GoalSourceType.MilestoneTasks, GoalSourceType.HealthWeight -> GoalSourceAvailability.ComingSoon
    }
}
