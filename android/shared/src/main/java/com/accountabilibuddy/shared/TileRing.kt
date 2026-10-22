package com.accountabilibuddy.shared

/** The drift tick colour on the Tile's focus ring (red, as the drift marks on the PC's Focus row). */
const val DRIFT_COLOR: Long = 0xFFEF4444L

/** A clock tick on the 24-hour rings: its minute of the day and angle; [major] at 12, 3, 6 and 9 (AM and PM). */
data class RingTick(val minute: Int, val degrees: Float, val major: Boolean)

/**
 * A stretch on the watch Tile's 24-hour rings, in degrees: midnight at 0° (the top), clockwise. [ring] 0 is the plan
 * (the outer ring), 1 is focus with its drift ticks (the inner ring).
 */
data class TileArc(val startDegrees: Float, val sweepDegrees: Float, val color: Long, val ring: Int)

/**
 * The Tile's day ring, from the Today page's view: the plan's parts in their colours, the focus sessions, a 1° tick
 * per drift, and where the now tick sits. 0.25° per minute, so the whole day is one turn. A snapshot from another day
 * draws nothing, as on the Today page.
 */
object TileRing {
    private const val DEGREES_PER_MINUTE = 0.25f

    fun arcs(view: TodayView): List<TileArc> {
        val plan = view.plan.mapNotNull { arc(it, ring = 0) }
        val focus = view.focus.mapNotNull { arc(it, ring = 1) }
        val drift = view.driftMinutes.map { TileArc(it * DEGREES_PER_MINUTE, 1f, DRIFT_COLOR, ring = 1) }
        return plan + focus + drift
    }

    fun nowDegrees(nowMinute: Int): Float = nowMinute * DEGREES_PER_MINUTE

    /** A tick each hour, the ones every 3 hours longer, like a clock's (on the Tile and the Today page). */
    fun ticks(): List<RingTick> = (0 until 24).map { hour -> RingTick(hour * 60, hour * 60 * DEGREES_PER_MINUTE, hour % 3 == 0) }

    private fun arc(arc: RingArc, ring: Int): TileArc? {
        val minutes = arc.endMinute - arc.startMinute
        return if (minutes <= 0) null else TileArc(arc.startMinute * DEGREES_PER_MINUTE, minutes * DEGREES_PER_MINUTE, arc.color, ring)
    }
}
