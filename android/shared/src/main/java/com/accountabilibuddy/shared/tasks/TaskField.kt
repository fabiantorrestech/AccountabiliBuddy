package com.accountabilibuddy.shared.tasks

/**
 * The parts of a task that merge on their own (ADR 76; the PC's TaskField, in its order): each has its own stamp in
 * [TaskFieldStamps], so a change to one part on one device and to another part on another device both survive. Only [TITLE] and
 * [NOTE] can end in "Changed on both devices". [wire] is the stamp's name in the sync file.
 */
enum class TaskField(val wire: String) {
    TITLE("title"),
    NOTE("note"),
    STEPS("steps"),
    LABEL("label"),
    TAGS("tags"),

    /** The bin with its previous bin, position and done time (a move, a reorder, a tick). */
    BIN("bin"),

    /** The whole reminder list with every occurrence's state: one field, one stamp (Q80). */
    REMINDERS("reminders"),
    ESTIMATE("estimate"),

    /** The due date and time. */
    DUE("due"),
    HIGHLIGHT("highlight"),
    CANT_MISS("cantMiss"),
    DEVICES("devices"),
    CALENDAR("calendar"),
    NUMBER("number"),

    /** The day part it was put in the plan for (ADR 50). */
    PLAN("plan"),
}
