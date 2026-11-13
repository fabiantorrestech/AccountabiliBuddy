package com.accountabilibuddy.shared.tasks

/**
 * How long a toast of the Tasks page stays (bundle spec §14; the PC's `TaskToastLength` and `TaskToastTiming`): the kind decides.
 * Its timer bar runs down over [millis].
 */
enum class ShelfToastLength(val millis: Long) {
    /** A plain message: 4 seconds. */
    PLAIN(4_000),

    /** A message with Undo or an action: 6 seconds. */
    WITH_UNDO(6_000),

    /** An explanation (what a swap or an undo did, why something wasn't done): 7 seconds. */
    EXPLANATION(7_000),

    /** "N more reminders waiting": 8 seconds. */
    WAITING(8_000),
}
