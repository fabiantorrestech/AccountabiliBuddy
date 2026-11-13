package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.runtime.State
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import com.accountabilibuddy.shared.tasks.ShelfDrag
import com.accountabilibuddy.shared.tasks.ShelfHold
import com.accountabilibuddy.shared.tasks.ShelfHoldState

/**
 * What the page does with the one drag gesture ([shelfDrag]).
 *
 * @property canLift Whether a hold may lift a row now (not while a sheet is open or the Shelf is hidden).
 * @property onLift The hold on the task with this id is up: lift it (or the selection it belongs to). False: don't.
 * @property onMove The finger moved to this place on the screen.
 * @property onRelease The finger let go there.
 * @property onCancel The gesture was taken away (the system took the touch, the page went away).
 */
class ShelfDragHandlers(
    val canLift: () -> Boolean,
    val onLift: (id: String, at: Offset) -> Boolean,
    val onMove: (at: Offset) -> Unit,
    val onRelease: (at: Offset) -> Unit,
    val onCancel: () -> Unit,
)

/**
 * The drag of the phone's Tasks page (round 2 R7.3b; change note §2): a finger held still on a row for 450 ms
 * ([ShelfDrag.LIFT_MS]) lifts it; from then on every touch is the drag's until the finger lets go.
 *
 * It sits on the page itself and not on the row, so the drag goes on when the row it began on leaves the screen (the page turns
 * while a zone is held, the list scrolls). Before the hold is up nothing is taken: moving scrolls the list or swipes the row as
 * always, and any of that (the list or the row consuming the movement, or the finger leaving its spot) calls the hold off. Once
 * lifted the touches are consumed on their way down, so the row's tap, its swipe and the list's scroll never see them (letting go
 * doesn't tap the row).
 */
fun Modifier.shelfDrag(state: ShelfDragState, handlers: State<ShelfDragHandlers>): Modifier = pointerInput(state) {
    val slop = viewConfiguration.touchSlop
    awaitEachGesture {
        val down = awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
        if (!handlers.value.canLift()) return@awaitEachGesture
        val start = down.position + state.host
        if (state.blockers.values.any { it.contains(start) }) return@awaitEachGesture
        val row = state.rows.entries.firstOrNull { it.value.contains(start) }?.key ?: return@awaitEachGesture

        // The hold (ShelfHold decides): watched on the Initial pass, before any child, and never by "consumed": the row's own tap
        // handler consumes the down of every touch, which called every hold off at once.
        val hold = ShelfHold(slop)
        withTimeoutOrNull(ShelfDrag.LIFT_MS) {
            while (true) {
                val event = awaitPointerEvent(PointerEventPass.Initial)
                val change = event.changes.firstOrNull { it.id == down.id }
                if (change == null) { hold.onMove(Float.MAX_VALUE, 0L); break }
                val elapsed = change.uptimeMillis - down.uptimeMillis
                if (!change.pressed) { hold.onUp(elapsed); break }
                hold.onMove((change.position - down.position).getDistance(), elapsed)
                if (hold.state != ShelfHoldState.WAITING) break
            }
        } ?: hold.onTimeout()
        if (hold.state != ShelfHoldState.LIFTED) return@awaitEachGesture
        if (!handlers.value.onLift(row, start)) return@awaitEachGesture

        var finished = false
        try {
            while (true) {
                val event = awaitPointerEvent(PointerEventPass.Initial)
                val change = event.changes.firstOrNull { it.id == down.id }
                event.changes.forEach { it.consume() }
                if (change == null) break
                val at = change.position + state.host
                if (!change.pressed) {
                    finished = true
                    handlers.value.onRelease(at)
                    break
                }

                if (!state.travelled && (change.position - down.position).getDistance() > slop) state.travelled = true
                handlers.value.onMove(at)
            }
        } finally {
            if (!finished) handlers.value.onCancel()
        }
    }
}
