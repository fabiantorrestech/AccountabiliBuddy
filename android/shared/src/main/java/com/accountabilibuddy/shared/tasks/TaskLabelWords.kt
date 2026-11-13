package com.accountabilibuddy.shared.tasks

/** The words on the phone's Labels screen (the PC's "3 tasks · rings: all devices"). */
object TaskLabelWords {
    /** "1 task", "3 tasks". */
    fun uses(count: Int): String = if (count == 1) "1 task" else "$count tasks"

    /** The line under a label's name: its tasks and where its reminders ring ([ringOn] null: every device; [phoneCount]: the phones that could). */
    fun subtitle(uses: Int, ringOn: List<String>?, phoneCount: Int): String {
        val rings = when {
            ringOn == null -> "all devices"
            ringOn.isEmpty() -> "PCs only"
            else -> "${ringOn.size} of $phoneCount phones"
        }
        return uses(uses) + " · rings: " + rings
    }
}
