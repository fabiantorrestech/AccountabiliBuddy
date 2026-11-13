package com.accountabilibuddy.companion.today

/** Where the one-time "Steps now live in Goals" card is (TD1): not decided yet, to be shown, or finished (added, dismissed, never on Today). */
enum class StepsMovedNote(val code: Int) {
    NotChecked(0),
    Show(1),
    Done(2),
    ;

    companion object {
        fun fromCode(code: Int): StepsMovedNote = entries.firstOrNull { it.code == code } ?: NotChecked
    }
}

/**
 * The rules of the one-time card on Today: "Steps now live in Goals · Add a steps goal" (one tap makes a Steps goal, never by itself).
 * Daily steps left Today in TD1; an install whose Today showed steps and that still tracks them should be told where they went.
 */
object StepsMovedRules {
    /** The key the old Today layout used for the steps part. */
    const val OLD_PART_KEY = "daily-steps"

    /**
     * Decided once, when this version first reads the saved layout: the card is to be shown unless the person had hidden the steps
     * part (a saved place `daily-steps:h`). [savedPlacements] is the saved "todayPlacements" text of the not-in-a-session layout.
     */
    fun initial(savedPlacements: String?): StepsMovedNote {
        val hidden = savedPlacements.orEmpty().split(',').any { pair ->
            val parts = pair.split(':')
            parts.size == 2 && parts[0].trim() == OLD_PART_KEY && parts[1].trim() == TodayPlacement.Hidden.key
        }
        return if (hidden) StepsMovedNote.Done else StepsMovedNote.Show
    }

    /** The card shows when it is to be shown, "Track daily steps" is on, no steps goal exists yet, and no session runs. */
    fun shows(note: StepsMovedNote, trackStepsOn: Boolean, hasStepsGoal: Boolean, inSession: Boolean): Boolean =
        note == StepsMovedNote.Show && trackStepsOn && !hasStepsGoal && !inSession
}
