package com.accountabilibuddy.shared.sync

import java.time.OffsetDateTime

/**
 * The rules every field-by-field merge shares (ADR 76; the PC's TaskFieldMerge, TaskLabelMerge, TaskAccountSettingsMerge and
 * TaskEdits): which of two stamps is the newer one, the stamp of a local change, and the future clamp. One implementation, so
 * tasks, labels, the account's settings and (ADR 79) goals can't drift apart.
 */
object FieldStamps {
    /**
     * Whether [first] is the newer stamp: the later time, then the device id (as text), then the value (as text), so every device
     * picks the same whatever order the copies come in. Equal in all three: the first.
     */
    fun firstIsNewer(first: FieldStamp, second: FieldStamp, firstValue: String, secondValue: String): Boolean {
        val byTime = DotNetTime.compare(first.at, second.at)
        if (byTime != 0) {
            return byTime > 0
        }

        val byDevice = first.by.compareTo(second.by)
        if (byDevice != 0) {
            return byDevice > 0
        }

        return firstValue >= secondValue
    }

    /** As [firstIsNewer] where a value may never have been chosen: a stamped value beats an unstamped one (neither stamped: the second). */
    fun firstWins(first: FieldStamp?, second: FieldStamp?, firstValue: String, secondValue: String): Boolean {
        if (first == null || second == null) {
            return first != null
        }

        return firstIsNewer(first, second, firstValue, secondValue)
    }

    /**
     * The next-stamp rule: the time of a local change made at [now] to something last stamped at [held] (each in turn; null = never):
     * [now], or one tick past a held time that isn't earlier, so a change made right after a faster clock's edit still wins.
     */
    fun next(now: OffsetDateTime, vararg held: OffsetDateTime?): OffsetDateTime {
        var at = now
        for (time in held) {
            if (time != null && DotNetTime.compare(time, at) >= 0) {
                at = DotNetTime.plusTick(time)
            }
        }

        return at
    }

    /** A stamp later than [limit] (a fast clock) counts as made at [limit]; its bases stay. */
    fun clamped(stamp: FieldStamp, limit: OffsetDateTime): FieldStamp = if (DotNetTime.compare(stamp.at, limit) > 0) stamp.copy(at = limit) else stamp
}
