package com.accountabilibuddy.companion.sync

import java.time.Duration
import java.time.Instant

/**
 * When the phone tells Autosync to sync (the "Tell Autosync to sync" setting), and nothing else: pure, so the timing can be checked
 * without Android. Three cases only:
 * - after a lunch change here: at once, then the folder is read again after [FOLLOW_UPS];
 * - while lunch runs, before each folder read, at most every [RUNNING_GAP];
 * - in the window where a lunch started on another device is expected, at most every [EXPECTED_GAP].
 * Every nudge counts for the gaps of the other two.
 */
class AutosyncNudgePolicy {
    private var lastNudge: Instant? = null
    private var lastOwnChangeNudge: Instant? = null

    /**
     * A lunch started, changed or ended here and our file is written. Returns how long to wait before telling Autosync: [Duration.ZERO] is
     * now; a press inside [OWN_CHANGE_GAP] of the last nudge waits out the gap and then nudges once (so "+5" then "I'm back" a few seconds
     * apart is not lost); null means a nudge is already waiting, which will cover this change too.
     */
    @Synchronized
    fun afterOwnChange(now: Instant): Duration? {
        val last = lastOwnChangeNudge
        if (last != null && now < last) {
            return null
        }

        val due = if (last == null) now else maxOf(now, last.plus(OWN_CHANGE_GAP))
        lastOwnChangeNudge = due
        lastNudge = due
        return Duration.between(now, due)
    }

    /** About to read the sync folder: true when Autosync should be told first. */
    @Synchronized
    fun beforeFolderRead(now: Instant, lunchRunning: Boolean, lunchExpected: Boolean): Boolean {
        val gap = when {
            lunchRunning -> RUNNING_GAP
            lunchExpected -> EXPECTED_GAP
            else -> return false
        }
        val last = lastNudge
        if (last != null && Duration.between(last, now) < gap) {
            return false
        }

        lastNudge = now
        return true
    }

    companion object {
        /** After a nudge for our own change, the folder is read again this long after. */
        val FOLLOW_UPS: List<Duration> = listOf(Duration.ofSeconds(20), Duration.ofSeconds(60))

        /** Presses of +5 / −5 in quick succession tell Autosync once. */
        val OWN_CHANGE_GAP: Duration = Duration.ofSeconds(10)

        val RUNNING_GAP: Duration = Duration.ofMinutes(2)
        val EXPECTED_GAP: Duration = Duration.ofMinutes(5)
    }
}
