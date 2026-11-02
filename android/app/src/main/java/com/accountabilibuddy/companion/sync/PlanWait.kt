package com.accountabilibuddy.companion.sync

import java.time.Instant

/**
 * A plan edit the PC took but is holding (ADR 28: it frees part of today's work hours, so it waits like any looser change).
 * The PC keeps the wait and applies it on its own; the phone only tells the user. [appliesAt] is worked out from the time
 * of the PC's file that carried its answer plus the wait it named, so it is "about" that time (the PC's own banner has the
 * real one). [seen] is set once the user has dismissed the sheet that says so.
 */
data class PlanWait(val pcName: String, val minutes: Int, val appliesAt: Instant, val seen: Boolean = false)
