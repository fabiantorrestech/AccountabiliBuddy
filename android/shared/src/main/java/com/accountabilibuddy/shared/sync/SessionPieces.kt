package com.accountabilibuddy.shared.sync

import com.accountabilibuddy.shared.session.PhoneAway
import com.accountabilibuddy.shared.session.PhoneAwayKind
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import java.time.Duration
import java.time.Instant
import java.time.ZoneId

/**
 * What a phone writes into its sync file about its own sessions (ADR 85, plan section 2): the Kotlin twin of Core's `SyncFileBuilder.Build`
 * (piece cutting) and the `running` card, parity-checked against the `ps1/pieces` fixture. Pure.
 *
 * Only what a PC would write travels: times, the profile's NAME, the checklist (ids as GUID text, local tasks as stubs) and
 * `offMinutesBefore`. Never app names, package lists, LMP or Curbox names, pickups, or an end reason.
 */
object SessionPieces {
    /** The file speaks for this many days of counted sessions (Core's `SyncMerge.FileSpan`). */
    val FILE_SPAN: Duration = SyncMerge.FILE_SPAN

    /** What a receiver keeps (Core's `SyncMerge.KeptFor`). */
    val KEPT_FOR: Duration = SyncMerge.KEPT_FOR

    /** The sessions part of the file and the time it speaks from. */
    data class Built(val sessions: List<SyncSession>, val coversFrom: Instant)

    /**
     * The file's sessions for the last 14 local days: the counted sessions that reach into them, cut into the pieces the user was there
     * for. The running one (no end) goes whole except for time with the phone off; its last piece stays open. An ended one is cut at
     * every away stretch. The checklist goes on the first piece only; the piece after a device-off gap carries its minutes.
     *
     * Test sessions never go. A running session counts only when it is the newest recorded one and has started (Core's `CountedRecords`).
     *
     * @param records The phone's session records, any order.
     * @param oldestSessionStart The start of the oldest record the phone still keeps, of any kind; null for none. When it is not older than
     * the 14 days the file speaks for all a receiver keeps, so receivers drop what was deleted.
     */
    fun build(records: List<PhoneSessionRecord>, now: Instant, zone: ZoneId, oldestSessionStart: Instant?): Built {
        val from = now.minus(FILE_SPAN).atZone(zone).toLocalDate().atStartOfDay(zone).toInstant()
        val coversFrom = if (oldestSessionStart != null && oldestSessionStart.isBefore(from)) from else now.minus(KEPT_FOR)
        val real = records.filter { !it.isTest }
        val newest = real.maxByOrNull { it.startedAt }
        val sessions = real
            .mapNotNull { record -> counted(record, newest, now) }
            .filter { it.end.isAfter(from) }
            .sortedBy { it.record.startedAt }
            .flatMap { piecesOf(it) }
            .filter { it.end == null || it.end.isAfter(from) }
        return Built(sessions, coversFrom)
    }

    /**
     * What a phone whose session log can't be opened writes (B5: "can't open" is not "empty"): no sessions and `coversFrom` = the time the
     * file is written, which speaks for nothing, so receivers keep every session they hold. The file also carries no `running`.
     */
    fun whenLogUnreadable(writtenAt: Instant): Built = Built(emptyList(), writtenAt)

    /**
     * The tasks of the session running in [sessions] (a device's file), for a reader of another device's session: the open piece's checklist
     * (the writer carries it on the first piece and on the open one), with the stubs of local-only tasks left out ([SessionTaskItem.visible]):
     * a reader never shows one as a blank open task. No session running, or one with no checklist, gives none: an earlier session's list
     * is never lent to it.
     */
    fun runningTasks(sessions: List<SyncSession>): List<SessionTaskItem> =
        SessionTaskItem.visible(sessions.lastOrNull { it.end == null }?.tasks.orEmpty())

    /**
     * The `running` card for [record], or null when it is a test or has ended (R14: the phone's profile NAME, the phase's start and end, no
     * break, `awaySince` only while the timer is paused (Pause timer, lunch, device off; never for a step away, ADR 77), the current task).
     */
    fun runningOf(record: PhoneSessionRecord): SyncRunning? {
        if (record.isTest || !record.isRunning) return null
        val pausedSince = record.openAway?.takeIf { it.kind.pausesTimer }?.from
        return SyncRunning(
            profile = record.profileName,
            phaseStartedAt = record.phaseStartedAt,
            phaseEndsAt = record.phaseEndsAt,
            isBreak = false,
            awaySince = pausedSince,
            currentTask = guidOf(record.currentTask),
            sessionStartedAt = record.startedAt,
        )
    }

    /**
     * The tasks as they go into a session piece (B11): ids as lower-case GUID text (a task or step with another id is left out, because the
     * PC refuses a whole file that has one); a task kept local to a device goes as its stub (no text, `localTo`); a category id that is not
     * a GUID is dropped with its name and colour. Estimate, note and day part are not in [SessionTaskItem] and never travel.
     */
    fun forSync(tasks: List<SessionTaskItem>): List<SessionTaskItem> = tasks.mapNotNull { task ->
        val id = guidOf(task.id) ?: return@mapNotNull null
        if (task.localTo != null) return@mapNotNull SessionTaskItem(id, "", null, emptyList(), localTo = task.localTo)
        val category = guidOf(task.categoryId)
        task.copy(
            id = id,
            subtasks = forSync(task.subtasks).map { it.copy(subtasks = emptyList()) },
            categoryId = category,
            category = task.category.takeIf { category != null },
            categoryColor = task.categoryColor.takeIf { category != null },
        )
    }

    /** True when the PC can read [tasks] as they are: every task and step has an id in GUID form (the PC refuses the whole file otherwise). */
    fun readableByPc(tasks: List<SessionTaskItem>): Boolean =
        tasks.all { guidOf(it.id) != null && (it.categoryId == null || guidOf(it.categoryId) != null) && readableByPc(it.subtasks) }

    // --- Cutting ----------------------------------------------------------------------------------------------------------------

    private class Counted(val record: PhoneSessionRecord, val end: Instant, val running: Boolean)

    private fun counted(record: PhoneSessionRecord, newest: PhoneSessionRecord?, now: Instant): Counted? {
        val ended = record.endedAt
        return when {
            ended != null -> if (ended.isAfter(record.startedAt)) Counted(record, ended, running = false) else null
            record === newest && now.isAfter(record.startedAt) -> Counted(record, now, running = true)
            else -> null
        }
    }

    private fun piecesOf(counted: Counted): List<SyncSession> {
        val record = counted.record
        val tasks = forSync(record.checklist.tasks)
        // Every away stretch is cut, running or ended (pause, lunch, step away, device off: never focus anywhere, owner "cut paused time",
        // PS1a review B1). A stretch still going ends the piece before it where it began; the open piece that follows starts when the file
        // is written and counts nothing until the user is back.
        val cut = record.away
        if (cut.isEmpty()) {
            return listOf(SyncSession(record.startedAt, if (counted.running) null else counted.end, record.profileName, false, tasks))
        }

        val pieces = pieceTimes(record.startedAt, counted.end, cut).toMutableList()
        if (counted.running && (pieces.isEmpty() || pieces.last().second != counted.end)) pieces += counted.end to counted.end
        return pieces.mapIndexed { index, (start, end) ->
            SyncSession(
                start,
                if (counted.running && !end.isBefore(counted.end)) null else end,
                record.profileName,
                false,
                // The checklist rides on the first piece and on the open piece (the running session's current one), so a reader of another
                // device's session finds it there whatever was cut before.
                if (index == 0 || (counted.running && !end.isBefore(counted.end))) tasks else emptyList(),
                offMinutesBefore(cut, start, counted.end),
            )
        }
    }

    /** The parts of the session from [start] to [end] without [away], in order (Core's `SessionGaps.Pieces`); an open stretch lasts to [end]. */
    private fun pieceTimes(start: Instant, end: Instant, away: List<PhoneAway>): List<Pair<Instant, Instant>> {
        val pieces = mutableListOf<Pair<Instant, Instant>>()
        var at = start
        for (stretch in away.filter { (it.to ?: end).isAfter(start) && it.from.isBefore(end) }.sortedBy { it.from }) {
            if (stretch.from.isAfter(at)) pieces += at to stretch.from
            val to = stretch.to ?: end
            if (to.isAfter(at)) at = to
        }

        if (end.isAfter(at)) pieces += at to end
        return pieces
    }

    // ADR 60: the time with the phone off that ended where this piece starts; only times travel, never why. Rounds half up, the same on every device (30:30 is 31, 31:30 is 32; under a minute is none).
    private fun offMinutesBefore(cut: List<PhoneAway>, pieceStart: Instant, sessionEnd: Instant): Int? {
        val minutes = cut.filter { it.kind == PhoneAwayKind.DEVICE_OFF && (it.to ?: sessionEnd) == pieceStart }
            .sumOf { Duration.between(it.from, it.to ?: sessionEnd).toMillis() / 60_000.0 }
        return if (minutes >= 1) Math.round(minutes).toInt() else null
    }

    private val guidForm = Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")

    private fun guidOf(text: String?): String? = text?.takeIf { guidForm.matches(it) }?.lowercase()
}
