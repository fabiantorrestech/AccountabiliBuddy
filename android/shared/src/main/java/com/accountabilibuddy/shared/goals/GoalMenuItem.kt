package com.accountabilibuddy.shared.goals

/**
 * One row of the goal menu: its [title], a muted [note] under it, a right-aligned [value] ("4 set", "Learning"), whether it can be
 * used ([enabled], greyed otherwise), a switch or tick ([checked], null for none), a destructive row ([danger]) and a row that opens
 * more ([opensMore], drawn with ›).
 */
data class GoalMenuItem(
    val id: GoalMenuItemId,
    val title: String,
    val note: String? = null,
    val value: String? = null,
    val enabled: Boolean = true,
    val checked: Boolean? = null,
    val danger: Boolean = false,
    val opensMore: Boolean = false,
)
