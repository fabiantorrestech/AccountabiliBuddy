package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant

/**
 * Keeps this device's copy of another device's part (ADR 22), as on the PC (Core's SyncMerge). A file speaks for every
 * session of its sender that ends after [coveredFrom] (a full file for its 14 local days, a QR file from the end of the
 * newest session it leaves out, and either one for all a receiver keeps when the sender has nothing older, as after
 * Delete in Setup): the copy keeps what ended before that and takes the file's sessions and drift for the rest. So the
 * same day arriving twice, by any route, leaves one copy of each session, and an edit, an omission or a deletion on the
 * sender reaches the copy. Only files the sender sealed are merged: a merged copy has gaps where its files didn't meet,
 * so it is never relayed (a carrier passes on the sender's own sealed bytes). Received history is kept for 90 days, like
 * the device's own, and a sender with Activity history off has none (ADR 15).
 */
object SyncMerge {
    /** How far back a file that doesn't say what it covers, and has no sessions, speaks for. */
    val FILE_SPAN: Duration = Duration.ofDays(14)

    /** How long received sessions and drift are kept. */
    val KEPT_FOR: Duration = Duration.ofDays(90)

    /**
     * The new copy, with the incoming file's card and coversFrom, its plan (else the stored one) and its settings (each
     * one it leaves out kept from the copy); [stored] itself when the incoming file was written before it.
     *
     * @param stored This device's copy of the sender's part; null for the first file from it.
     * @param incoming A file the sender sealed, by any method (also when another device carries it).
     * @throws IllegalArgumentException The stored copy is another device's.
     */
    fun merge(stored: SyncFile?, incoming: SyncFile, now: Instant): SyncFile {
        require(stored == null || stored.device.deviceId == incoming.device.deviceId) { "The stored copy is another device's." }
        if (stored != null && incoming.device.writtenAt.isBefore(stored.device.writtenAt)) {
            return stored
        }

        val sessions = mutableListOf<SyncSession>()
        val drift = mutableListOf<SyncDrift>()
        if (!incoming.device.historyOff) {
            // The file holds its sessions whole: a stored copy of one with an earlier end (it was running then) goes too.
            val covered = coveredFrom(incoming)
            val earliestSession = incoming.sessions.minOfOrNull { it.start }
            val sessionsFrom = if (earliestSession != null && earliestSession.isBefore(covered)) earliestSession else covered

            // A drift mark joined across a session's start can begin before the first session: the marks it covers go.
            val earliestMark = incoming.drift.minOfOrNull { it.start }
            val driftFrom = if (earliestMark != null && earliestMark.isBefore(sessionsFrom)) earliestMark else sessionsFrom
            if (stored != null) {
                // A session that was running when the copy was written is taken as ending then, as it was shown.
                stored.sessions
                    .map { if (it.end == null) it.copy(end = stored.device.writtenAt) else it }
                    .filterTo(sessions) { !it.end!!.isAfter(sessionsFrom) }
                stored.drift.filterTo(drift) { !it.end.isAfter(driftFrom) }
            }

            sessions += incoming.sessions
            drift += incoming.drift
        }

        val keptFrom = now.minus(KEPT_FOR)
        return incoming.copy(
            sessions = sessions.filter { !(it.end ?: incoming.device.writtenAt).isBefore(keptFrom) }.sortedBy { it.start },
            drift = drift.filter { !it.end.isBefore(keptFrom) }.sortedBy { it.start },
            dayPlan = incoming.dayPlan ?: stored?.dayPlan,
            documents = mergeDocuments(stored?.documents.orEmpty(), incoming.documents),
        )
    }

    /**
     * The time [file] speaks from: its coversFrom; for a file that doesn't say, its earliest session, else [FILE_SPAN]
     * before it was written.
     */
    fun coveredFrom(file: SyncFile): Instant =
        file.coversFrom ?: file.sessions.minOfOrNull { it.start } ?: file.device.writtenAt.minus(FILE_SPAN)

    /** The incoming settings, plus each stored one it leaves out (a QR file can drop them to fit its codes). */
    private fun mergeDocuments(stored: List<SyncDocument>, incoming: List<SyncDocument>): List<SyncDocument> =
        incoming + stored.filter { document -> incoming.none { it.key == document.key } }
}
