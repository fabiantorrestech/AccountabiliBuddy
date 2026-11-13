package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import java.time.OffsetDateTime

/** How long a task went untouched (the PC's TaskUntouched): whole weeks since its last edit, move, step tick or opening. */
object TaskUntouched {
    const val DEFAULT_WEEKS = 3
    private const val TICKS_PER_DAY = 864_000_000_000L

    /** Whole weeks since it was touched (0 for a task touched in the future). */
    fun weeks(task: TaskItem, now: OffsetDateTime): Int =
        maxOf(0L, Math.floorDiv(DotNetTime.ticks(now) - DotNetTime.ticks(task.touchedAt), 7 * TICKS_PER_DAY)).toInt()

    fun isOlderThan(task: TaskItem, weeks: Int, now: OffsetDateTime): Boolean =
        DotNetTime.ticks(now) - DotNetTime.ticks(task.touchedAt) >= 7L * weeks * TICKS_PER_DAY

    /** An open Shelf task nobody touched for [weeks] weeks (what the weekly tidy-up asks about). */
    fun isUntouched(task: TaskItem, weeks: Int, now: OffsetDateTime): Boolean =
        task.isLive && !task.isDone && TaskBins.isShelf(task.bin) && isOlderThan(task, weeks, now)
}
