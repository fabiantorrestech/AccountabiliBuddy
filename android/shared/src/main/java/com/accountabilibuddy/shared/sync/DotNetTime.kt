package com.accountabilibuddy.shared.sync

import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneOffset

/**
 * Times as the PC keeps them (.NET's DateTimeOffset): a moment in 100 ns ticks with the offset it was written with. Two times are
 * compared by their moment only, as the PC compares them; the offset is kept so a time read from the PC is written back as it came.
 */
object DotNetTime {
    /** What the PC reads a missing time as (default(DateTimeOffset)). */
    val MIN: OffsetDateTime = OffsetDateTime.of(1, 1, 1, 0, 0, 0, 0, ZoneOffset.UTC)

    /** The Unix epoch, the PC's DateTimeOffset.UnixEpoch. */
    val UNIX_EPOCH: OffsetDateTime = OffsetDateTime.of(1970, 1, 1, 0, 0, 0, 0, ZoneOffset.UTC)

    /** The moment in .NET ticks (100 ns since 0001-01-01 UTC). */
    fun ticks(time: OffsetDateTime): Long = time.toInstant().ticks()

    /** The time cut to whole ticks: a finer fraction is dropped, as the PC would keep it. */
    fun cut(time: OffsetDateTime): OffsetDateTime = if (time.nano % 100 == 0) time else time.withNano(time.nano / 100 * 100)

    /** A time of day cut to whole ticks. */
    fun cut(time: LocalTime): LocalTime = if (time.nano % 100 == 0) time else time.withNano(time.nano / 100 * 100)

    /** One tick later, the offset kept (the PC's AddTicks(1)). */
    fun plusTick(time: OffsetDateTime): OffsetDateTime = time.plusNanos(100)

    fun compare(first: OffsetDateTime, second: OffsetDateTime): Int = ticks(first).compareTo(ticks(second))

    /** The same moment (whatever the offsets), the PC's equality of two DateTimeOffsets. */
    fun same(first: OffsetDateTime?, second: OffsetDateTime?): Boolean =
        if (first == null || second == null) first == null && second == null else ticks(first) == ticks(second)

    /** [value], or [limit] when [value] is later (the PC's future clamp keeps [limit]'s offset). */
    fun atMost(value: OffsetDateTime, limit: OffsetDateTime): OffsetDateTime = if (compare(value, limit) > 0) limit else value
}
