package com.accountabilibuddy.shared.sync

/**
 * A step's letter from its place among all of its task's steps (ADR 76), as the PC's `TaskStepLetters.Of`: 0 = "a", 25 = "z",
 * 26 = "aa". Today's Done reads "Done · b" for the step it ticks, the same as the PC's session card (round 2, Q352).
 */
object TaskStepLetters {
    /** The letter for the step at a 0-based place. */
    fun of(index: Int): String {
        require(index >= 0) { "A step's place is 0 or more." }
        val letters = StringBuilder()
        var number = index + 1
        while (number > 0) {
            number--
            letters.insert(0, ('a' + number % 26))
            number /= 26
        }

        return letters.toString()
    }
}
