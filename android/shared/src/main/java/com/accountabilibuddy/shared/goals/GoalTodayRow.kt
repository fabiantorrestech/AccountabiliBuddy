package com.accountabilibuddy.shared.goals

import java.util.UUID

/**
 * One goal on the phone's "Today's goals" (boards Main, B-Phone-Tray-Open): its name with the icon and colour of its chip, the
 * muted line under it ("Learning · 12-day streak", "Not logged since Mon", "1h 10m of 2h · 50 min to go"), and at the right the
 * control of its kind. Plain values for the screen to draw; nothing here is stored. The name is the user's private text.
 *
 * @property number "8/15" beside a habit's tick when it ticks itself at a number of minutes; null otherwise.
 * @property checked A habit ticked today.
 * @property fraction The bar's share, 0 to 1 (0 when there is no bar).
 * @property done Nothing is left to do for it today: it doesn't count toward "n left".
 */
data class GoalTodayRow(
    val goalId: UUID,
    val name: String,
    val icon: String,
    val color: String,
    val subLine: String,
    val control: GoalTodayControl,
    val checked: Boolean,
    val number: String?,
    val fraction: Double,
    val done: Boolean,
    val isDeepWork: Boolean,
)
