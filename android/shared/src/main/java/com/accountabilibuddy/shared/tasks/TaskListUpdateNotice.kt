package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import java.time.Duration
import java.time.Instant

/**
 * "Update AccountabiliBuddy on <PC>" (R7.2 review item 3): a PC whose file shares the old task lists but carries no `tasks` part is
 * older than the one task list (ADR 76), and a phone on the task list ignores its task edits (Q583). The release order is "PC first";
 * this names the PCs that are still behind, so the phone can say so quietly.
 *
 * It is decided from what each PC's file carried: the copies the phone keeps of received files hold no task parts, only marks (an
 * empty `parked` list for "shares the lists", an empty `tasks` list for "writes the one task list",
 * [com.accountabilibuddy.shared.sync.SyncFileStrip.tasksMark]), and both a whole file and a kept copy read the same way here.
 *
 * The notice lists each device on a line of its own and can be closed (×). Closed, it comes back after [SNOOZE] while any listed
 * device is still behind, at once when a device that wasn't listed falls behind, and it goes by itself when none is.
 */
object TaskListUpdateNotice {
    /** How long a closed notice stays away while the same devices are still behind. */
    val SNOOZE: Duration = Duration.ofDays(3)

    /** The notice's first line; the devices follow, one per line. */
    const val HEADING = "Update AccountabiliBuddy on:"

    /** The PCs among [files] that share old task lists without `tasks`, in the files' order, each once (by device id). */
    fun pcsBehind(files: List<SyncFile>): List<SyncDeviceCard> = files
        .filter { file ->
            file.device.kind.equals(SyncDeviceCard.KIND_PC, ignoreCase = true) &&
                file.taskParts?.tasks == null &&
                (file.nextTasks != null || file.later != null || file.parked != null)
        }
        .map { it.device }
        .distinctBy { it.deviceId }

    /** The names of [pcsBehind], each once. */
    fun pcsWithoutTasks(files: List<SyncFile>): List<String> = pcsBehind(files).map { it.name }.distinct()

    /** The notice's lines: the heading, one line per device, and why; none when no device is behind. */
    fun lines(names: List<String>): List<String> =
        if (names.isEmpty()) emptyList() else listOf(HEADING) + names + why(names.size)

    /** The last line: what being behind means. */
    fun why(count: Int): String =
        if (count == 1) "Its task edits don't reach this phone until it is updated." else "Their task edits don't reach this phone until they are updated."

    /**
     * Whether the notice shows at [now] for the devices [behind] (their ids): never without one; always when it was never closed;
     * closed, again when a device is behind that wasn't when it was closed ([dismissedFor]) or [SNOOZE] has passed since
     * [dismissedAt]. A close stamped in the future (the clock was moved back) hides it; the page pulls such a stamp back to now
     * ([dismissedAt]), so it never hides the notice for longer than [SNOOZE].
     */
    fun shows(behind: List<String>, dismissedFor: List<String>, dismissedAt: Instant?, now: Instant): Boolean {
        if (behind.isEmpty()) return false
        if (dismissedAt == null) return true
        val known = dismissedFor.map { it.lowercase() }.toSet()
        if (behind.any { it.lowercase() !in known }) return true
        val since = if (dismissedAt.isAfter(now)) Duration.ZERO else Duration.between(dismissedAt, now)
        return since >= SNOOZE
    }

    /** What is remembered of a close at [now]: the devices behind then; a close stamped in the future is pulled back to [now]. */
    fun dismissedAt(stored: Instant?, now: Instant): Instant? = stored?.let { if (it.isAfter(now)) now else it }

    /** The notice as one text (the heading, the devices and why, each on its own line); null when there is none. */
    fun text(names: List<String>): String? = lines(names).takeIf { it.isNotEmpty() }?.joinToString("\n")
}
