package com.accountabilibuddy.shared.goals

/**
 * The phone's half of the goal rules (plan §3b 5, 6, 7), for R6's screens; nothing here changes how a goal counts.
 * - Deep work's daily target is read only on the phone (ADR 29: the daily goal is set on the PC; there is no goal-edit sync action).
 * - #tag rules are counted on the PC only until ADR 79: the phone counts with [GoalSourceData.forPhone] (tags off) and labels
 *   such a source [MEASURED_ON_PC].
 * - The Google Calendar category source is PC only (plan §3b 7): the phone has no calendar windows for goals.
 */
object GoalPhoneRules {
    const val MEASURED_ON_PC = "Measured on your PC"

    /** Whether the phone may change [goal]'s daily (or per-period) target: never Deep work's (ADR 29). */
    fun canEditDailyTarget(goal: Goal): Boolean = !goal.isDeepWork

    /** Whether the phone counts a source of [type] itself (everything but the PC-only and "Coming soon" ones). */
    fun countsOnPhone(type: GoalSourceType): Boolean = when (GoalSources.availabilityOf(type)) {
        GoalSourceAvailability.Everywhere, GoalSourceAvailability.PhoneOnly -> true
        GoalSourceAvailability.PcOnly, GoalSourceAvailability.ComingSoon -> false
    }

    /** Whether the phone shows a source of [type] as [MEASURED_ON_PC] (tags, calendar, Beeminder). */
    fun measuredOnPc(type: GoalSourceType): Boolean = GoalSources.availabilityOf(type) == GoalSourceAvailability.PcOnly
}
