package com.accountabilibuddy.shared.goals

/**
 * Settings > Goals > Default reminders (the PC's `GoalReminderDefaults`, brief 5.10; device-local): the rule switches a new goal
 * reminder starts with. "Nudge when the week falls behind" has one default per mode (on in AccountabiliBuddy mode, off in
 * ProductaBuddy mode, ADR 69). Suggestions only.
 */
data class GoalReminderDefaults(
    val skipWhenDone: Boolean = true,
    val nudgeAccountabiliBuddy: Boolean = true,
    val nudgeProductaBuddy: Boolean = false,
) {
    /** The nudge default in the mode the app is in. */
    fun nudgeFor(productaBuddy: Boolean): Boolean = if (productaBuddy) nudgeProductaBuddy else nudgeAccountabiliBuddy
}
