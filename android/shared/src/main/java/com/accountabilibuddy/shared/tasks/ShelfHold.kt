package com.accountabilibuddy.shared.tasks

/** What the gesture on a row has become ([ShelfHold]). */
enum class ShelfHoldState {
    /** Finger down on a row, not yet decided. */
    WAITING,

    /** The hold was up with the finger still: the row is lifted. */
    LIFTED,

    /** The lifted row's finger has moved: a drag. */
    DRAGGING,

    /** Not a hold: a tap, a swipe of the row, a scroll or a page swipe (those carry on by themselves). */
    CALLED_OFF,

    /** Lifted and let go: see [ShelfHold.outcome]. */
    DONE,
}

/** How a gesture ended. */
enum class ShelfHoldOutcome { NONE, TAP, MOVED_ON, SELECT, DROP }

/**
 * The arbitration of one touch on a Shelf row (round 2 R7.3b, fixed after the first device test): pure, so the checks can drive it.
 * It looks only at time, distance moved and whether the finger is down, never at whether a child (the row's tap, a swipe, the
 * list) consumed the event: a row's own tap handler consumes the *down* of every touch, so reading "consumed" called every hold off
 * at once. A move past the touch slop is what hands the touch to a swipe or a scroll, and a lift of the finger before the time is
 * up is a tap.
 */
class ShelfHold(private val slop: Float, private val liftMs: Long = ShelfDrag.LIFT_MS) {
    var state: ShelfHoldState = ShelfHoldState.WAITING
        private set
    var outcome: ShelfHoldOutcome = ShelfHoldOutcome.NONE
        private set

    /** The finger moved [distance] away from where it went down, at [elapsedMs] after it did. */
    fun onMove(distance: Float, elapsedMs: Long) {
        when (state) {
            ShelfHoldState.WAITING -> if (distance > slop) callOff(ShelfHoldOutcome.MOVED_ON) else if (elapsedMs >= liftMs) state = ShelfHoldState.LIFTED
            ShelfHoldState.LIFTED -> if (distance > slop) state = ShelfHoldState.DRAGGING
            else -> Unit
        }
    }

    /** The hold time ran out with the finger down and still. */
    fun onTimeout() {
        if (state == ShelfHoldState.WAITING) state = ShelfHoldState.LIFTED
    }

    /** The finger let go at [elapsedMs]. */
    fun onUp(elapsedMs: Long) {
        when (state) {
            ShelfHoldState.WAITING -> if (elapsedMs >= liftMs) { state = ShelfHoldState.LIFTED; finish(ShelfHoldOutcome.SELECT) } else callOff(ShelfHoldOutcome.TAP)
            ShelfHoldState.LIFTED -> finish(ShelfHoldOutcome.SELECT)
            ShelfHoldState.DRAGGING -> finish(ShelfHoldOutcome.DROP)
            else -> Unit
        }
    }

    private fun callOff(by: ShelfHoldOutcome) {
        state = ShelfHoldState.CALLED_OFF
        outcome = by
    }

    private fun finish(by: ShelfHoldOutcome) {
        state = ShelfHoldState.DONE
        outcome = by
    }
}

/** What a tap on a row's title does (a closed row's title is part of the row). */
object ShelfTitleTap {
    enum class Action { ROW, RENAME }

    fun of(expanded: Boolean, selecting: Boolean): Action = if (expanded && !selecting) Action.RENAME else Action.ROW
}
