package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant

/**
 * Keeps this device's copy of another device's part (ADR 22), as on the PC (Core's SyncMerge). A file speaks for
 * everything from its earliest session on (a QR file for today, a full one for its 14 days, a relayed copy for all it
 * holds): the copy keeps what ended before that and takes the file's sessions and drift for the rest. So the same day
 * arriving twice, by any route, leaves one copy of each session, and an edit or a deletion on the sender reaches the
 * copy. Received history is kept for 90 days, like the device's own, and a sender with Activity history off has none
 * (ADR 15).
 */
object SyncMerge {
    /** How far back a file without sessions speaks for; the PC's SyncFileBuilder always covers it. */
    val FILE_SPAN: Duration = Duration.ofDays(14)

    /** How long received sessions and drift are kept. */
    val KEPT_FOR: Duration = Duration.ofDays(90)

    /**
     * The new copy, with the incoming file's card, its plan (else the stored one) and its settings (each one it leaves
     * out kept from the copy); [stored] itself when the incoming file was written before it.
     *
     * @param stored This device's copy of the sender's part; null for the first file from it.
     * @param incoming A file from the sender, by any method (also a copy another device relays).
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
            val sessionsFrom = incoming.sessions.minOfOrNull { it.start } ?: incoming.device.writtenAt.minus(FILE_SPAN)

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

    /** The incoming settings, plus each stored one it leaves out (a QR file can drop them to fit its codes). */
    private fun mergeDocuments(stored: List<SyncDocument>, incoming: List<SyncDocument>): List<SyncDocument> =
        incoming + stored.filter { document -> incoming.none { it.key == document.key } }
}
