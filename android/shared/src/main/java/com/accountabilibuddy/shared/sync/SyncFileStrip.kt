package com.accountabilibuddy.shared.sync

/**
 * What the phone leaves out of a sync file, as the PC does (its SyncCoordinator's kept copy and SyncFileBuilder.ForQr): the shared
 * task lists and their tombstones (ADR 48, 72), ADR 76's task parts and the past keys (ADR 56) never go in the copy kept of another
 * device's file (they merge into this phone's own sealed stores, so their text isn't kept twice), nor in a QR file.
 */
object SyncFileStrip {
    /** The mark a kept copy holds for "this device writes the one task list" (ADR 76): an empty `tasks` part, nothing else. */
    val TASKS_MARK: com.accountabilibuddy.shared.tasks.TaskParts = com.accountabilibuddy.shared.tasks.TaskParts(emptyList(), null, null, null, null)

    /**
     * The copy kept of a received file. [parkedMark]: an empty `parked` list kept as the mark that the device keeps the shared lists
     * (null for none); [steps]: the step counts to keep (a QR file never carries them); [tasksMark]: whether the device writes
     * `tasks` ([tasksMark]): then an empty `tasks` list stays as the mark, never a task (the PC's own kept copy has no such mark, so
     * the default leaves it out).
     */
    fun keptCopy(file: SyncFile, parkedMark: List<SyncParked>?, steps: SyncSteps?, tasksMark: Boolean = false): SyncFile = file.copy(
        nextTasks = null, parked = parkedMark, later = null, pastKeys = null, steps = steps, taskHistory = null, pastSettled = null,
        autoSaved = null, handovers = null, handoverAcks = null, taskParts = if (tasksMark) TASKS_MARK else null,
        goalParts = null, goalYearsMerged = null, sessionNotes = null,
    )

    /**
     * Whether the copy kept of [file] gets the `tasks` mark. A file that carries lists or task parts decides by itself: the mark
     * when it has a `tasks` part (so a PC that went back to an older version loses it again). A file with neither (a QR file) keeps
     * what the copy [held] of that device's last file had, as the `parked` mark is kept. Without the mark every PC that shares its
     * lists would look older than the one task list, since the copy keeps the `parked` mark but none of the task parts.
     */
    fun tasksMark(file: SyncFile, held: SyncFile?): Boolean {
        val decides = file.taskParts != null || file.nextTasks != null || file.parked != null || file.later != null
        return if (decides) file.taskParts?.tasks != null else held?.taskParts?.tasks != null
    }

    /** This phone's own file as QR codes carry it: no shared lists, past keys, categories, steps, task parts or receipts (ADR 86: the codes' room goes to sessions). */
    fun forQr(file: SyncFile, zone: java.time.ZoneId = java.time.ZoneId.systemDefault()): SyncFile = todaysSessionsOnly(stripped(file), zone)

    /**
     * Only today's sessions go into QR codes (the PC's `SyncFileBuilder.ForQr`): the ones that end after the start of the day the file was written, the open one
     * ending when the file was written; `coversFrom` rises to the newest end left out (the file then speaks for what it carries, and the receiver keeps its older
     * copies); the drift follows what the file speaks for. The PC's further trimming to the codes' room is not ported.
     */
    private fun todaysSessionsOnly(file: SyncFile, zone: java.time.ZoneId): SyncFile {
        val writtenAt = file.device.writtenAt
        val todayStart = writtenAt.atZone(zone).toLocalDate().atStartOfDay(zone).toInstant()
        fun end(session: SyncSession) = session.end ?: writtenAt
        val kept = file.sessions.filter { end(it).isAfter(todayStart) }.sortedBy { it.start }
        val leftOutEnds = file.sessions.filter { !end(it).isAfter(todayStart) }.map(::end)
        val coversFrom = (listOf(SyncMerge.coveredFrom(file)) + leftOutEnds).max()
        val driftFrom = if (kept.isNotEmpty() && kept[0].start.isBefore(coversFrom)) kept[0].start else coversFrom
        return file.copy(sessions = kept, coversFrom = coversFrom, drift = file.drift.filter { it.end.isAfter(driftFrom) }.sortedBy { it.start })
    }

    private fun stripped(file: SyncFile): SyncFile = file.copy(nextTasks = null, parked = null, later = null, pastKeys = null, categories = null, steps = null, taskParts = null, goalParts = null, goalYearsMerged = null, receipts = null, sessionNotes = null)
}
