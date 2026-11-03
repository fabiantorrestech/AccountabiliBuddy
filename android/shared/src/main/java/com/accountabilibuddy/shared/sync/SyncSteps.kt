package com.accountabilibuddy.shared.sync

import java.time.Instant

/**
 * A phone's daily step counts as its sync file carries them (the PC's SyncSteps): the goal, when Health Connect was last read and the
 * counts of the last few days. Numbers and dates only. Written only by the phone that read them, and only while "Track daily steps" is
 * on; they go to the user's own devices and nowhere else, and the watch doesn't get them. Optional in the JSON, so older apps ignore it.
 *
 * @property goal The daily goal in steps.
 * @property readAt When Health Connect was read (the "updated" time); the PC takes the newest of the phones' files.
 * @property days At most [MAX_DAYS] days, oldest first; a day Health Connect gave nothing for is left out, never written as 0.
 */
data class SyncSteps(val goal: Int, val readAt: Instant, val days: List<SyncStepDay>) {
    companion object {
        const val MAX_DAYS = 14
        const val DEFAULT_GOAL = 8_000
        const val MAX_GOAL = 100_000
    }
}
