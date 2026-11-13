package com.accountabilibuddy.shared.tasks

/**
 * Where a task sits in the one shared task list (ADR 76; the PC's TaskBin): Today's three bins and the Shelf's three, in the PC's
 * order. [wire] is the name the sync file and the stores carry.
 */
enum class TaskBin(val wire: String) {
    /** Today, This session: the actions the next or running session works on (capped). */
    SESSION("session"),

    /** Today, Queued: waiting for a free place in the session. */
    QUEUED("queued"),

    /** Today, Finished: ticked; it goes to History when the day is over. */
    DONE("done"),

    /** Shelf, To sort: jotted thoughts, come-backs, auto-saved and parked tasks. */
    SORT("sort"),

    /** Shelf, Next: for the next few days. */
    NEXT("next"),

    /** Shelf, Later: some other day. */
    LATER("later"),
}
