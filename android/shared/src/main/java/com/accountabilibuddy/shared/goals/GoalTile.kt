package com.accountabilibuddy.shared.goals

/**
 * One tile of a goal's page (round 2, change note §4a "Tiles", the PC's `GoalTileViewModel`): Today, This period, Target (only with
 * a long-term target) and the optional Streak and Total. Plain values for the screen to draw; nothing here is stored.
 *
 * @property kind "today", "period", "target", "streak" or "total".
 * @property big "8", "34h", "Done".
 * @property of "of 15", "of 50h"; null for none.
 * @property caption "Minutes today", "Days this week · on pace".
 * @property foot "7 min to go · ticks itself at 15 min"; null for none.
 * @property fraction The bar's share, 0 to 1; null when the tile has no bar.
 * @property mark "Where you should be today" on the Target bar, 0 to 1; null for none.
 * @property status "On track", "Behind", "Met" (Target); null for none.
 * @property segments This period's days as cells (the week dots); null for none.
 */
data class GoalTile(
    val kind: String,
    val big: String,
    val of: String?,
    val caption: String,
    val foot: String?,
    val fraction: Double?,
    val mark: Double?,
    val status: String?,
    val statusAmber: Boolean,
    val segments: List<GoalStripCell>?,
) {
    val showsBar: Boolean get() = fraction != null
}
