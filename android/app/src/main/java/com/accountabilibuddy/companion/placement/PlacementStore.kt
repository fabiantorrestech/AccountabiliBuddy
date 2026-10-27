package com.accountabilibuddy.companion.placement

import android.content.Context
import com.accountabilibuddy.shared.sync.SyncPlacement
import java.time.Duration
import java.time.Instant

/**
 * The placement of the current (or last) PC session, kept in preferences "placement" so the sync file can be written
 * whether or not the detector is running: the state and since when, the counts, and when the last reading was. Only
 * this, never sensor values.
 *
 * The file never claims more than the phone knows: a state whose last reading is older than [STALE_AFTER], or from a
 * detector that is not running (break, session over, notification off, app killed), is written as unknown.
 */
class PlacementStore(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** One session's record. */
    data class Record(
        val sessionStart: Instant,
        val authoritative: Boolean,
        val state: PlacementState,
        val since: Instant,
        val lastReadingAt: Instant,
        val outCount: Int,
        val pocketMillis: Long,
        val active: Boolean,
        val endedAt: Instant?,
    )

    @Synchronized
    fun load(): Record? {
        val start = prefs.getLong(KEY_SESSION, 0L).takeIf { it > 0 }?.let(Instant::ofEpochMilli) ?: return null
        return Record(
            start,
            prefs.getBoolean(KEY_AUTHORITATIVE, false),
            PlacementState.fromWire(prefs.getString(KEY_STATE, null)),
            Instant.ofEpochMilli(prefs.getLong(KEY_SINCE, start.toEpochMilli())),
            Instant.ofEpochMilli(prefs.getLong(KEY_READING, start.toEpochMilli())),
            prefs.getInt(KEY_OUT, 0),
            prefs.getLong(KEY_POCKET, 0L),
            prefs.getBoolean(KEY_ACTIVE, false),
            prefs.getLong(KEY_ENDED, 0L).takeIf { it > 0 }?.let(Instant::ofEpochMilli),
        )
    }

    /**
     * The detector is running for [session] at [now]. A session with another start than the one on record is a new one
     * (counts from zero) unless one of the two starts is only a guess (the planning stage's) and the other the PC's
     * counted session: then it is the same session and the better start is kept. Returns the record.
     */
    @Synchronized
    fun begin(session: PlacementSession.Info, now: Instant): Record {
        val current = load()
        // A session that "ended" a moment ago with the same start was a flicker of the PC's word (a file gap).
        val endedJustNow = current?.endedAt?.let { Duration.between(it, now) <= REOPEN_WITHIN } ?: false
        val sameSession = current != null && (current.endedAt == null || endedJustNow) &&
            (PlacementSession.same(current.sessionStart, session.start) || (current.endedAt == null && !(current.authoritative && session.authoritative)))
        if (current == null || !sameSession) {
            save(Record(session.start, session.authoritative, PlacementState.Unknown, now, now, 0, 0L, true, null))
        } else {
            val start = if (session.authoritative && !current.authoritative) session.start else current.sessionStart
            save(current.copy(sessionStart = start, authoritative = current.authoritative || session.authoritative, active = true, endedAt = null))
        }

        return load()!!
    }

    /** The detector stopped (a break, quiet, the notification turned off): the state is unknown from now on; counts stay. */
    @Synchronized
    fun pause(now: Instant) {
        val current = load() ?: return
        if (current.active || current.state != PlacementState.Unknown) {
            save(current.copy(state = PlacementState.Unknown, since = now, lastReadingAt = now, active = false))
        }
    }

    /** The session is over: like [pause], and the counts stay for the session's review for a day. */
    @Synchronized
    fun end(now: Instant) {
        val current = load() ?: return
        if (current.endedAt == null) {
            save(current.copy(state = PlacementState.Unknown, since = now, lastReadingAt = now, active = false, endedAt = now))
        }
    }

    /** A classification at [now]. Pocket time since the last reading is added (not across a gap longer than [STALE_AFTER]). */
    @Synchronized
    fun reading(state: PlacementState, now: Instant) {
        val current = load() ?: return
        val gap = Duration.between(current.lastReadingAt, now)
        val pocket = if (current.state == PlacementState.Pocket && current.active && gap <= STALE_AFTER) current.pocketMillis + gap.toMillis() else current.pocketMillis
        val since = if (state != current.state || !current.active) now else current.since
        save(current.copy(state = state, since = since, lastReadingAt = now, pocketMillis = pocket, active = true))
    }

    /** A new stretch of the phone being out (in use or face up, no work use). */
    @Synchronized
    fun addOut() {
        val current = load() ?: return
        save(current.copy(outCount = current.outCount + 1))
    }

    /** The placement for this phone's sync file; null when there's none (no session recorded, or over for a day). */
    fun forSyncFile(now: Instant, workUse: WorkUseStore): SyncPlacement? {
        val record = load() ?: return null
        if (record.endedAt?.let { Duration.between(it, now) > KEEP_AFTER_END } == true) {
            return null
        }

        val stale = record.active && Duration.between(record.lastReadingAt, now) > STALE_AFTER
        val state = if (!record.active || stale) PlacementState.Unknown else record.state
        val since = if (stale) record.lastReadingAt.plus(STALE_AFTER) else record.since
        val workUseUntil = if (record.active) workUse.active(now)?.takeIf { PlacementSession.same(it.sessionStart, record.sessionStart) }?.end else null
        return SyncPlacement(state.wire, since, workUseUntil, record.outCount, (record.pocketMillis / 60_000L).toInt(), record.sessionStart)
    }

    private fun save(record: Record) {
        prefs.edit()
            .putLong(KEY_SESSION, record.sessionStart.toEpochMilli())
            .putBoolean(KEY_AUTHORITATIVE, record.authoritative)
            .putString(KEY_STATE, record.state.wire)
            .putLong(KEY_SINCE, record.since.toEpochMilli())
            .putLong(KEY_READING, record.lastReadingAt.toEpochMilli())
            .putInt(KEY_OUT, record.outCount)
            .putLong(KEY_POCKET, record.pocketMillis)
            .putBoolean(KEY_ACTIVE, record.active)
            .putLong(KEY_ENDED, record.endedAt?.toEpochMilli() ?: 0L)
            .apply()
    }

    companion object {
        /** A reading older than this is not believed: the state is written as unknown. */
        val STALE_AFTER: Duration = Duration.ofMinutes(3)

        /** A finished session's counts stay in the file this long (for the PC's session review). */
        val KEEP_AFTER_END: Duration = Duration.ofHours(24)
        private val REOPEN_WITHIN: Duration = Duration.ofMinutes(5)
        private const val PREFS = "placement"
        private const val KEY_SESSION = "sessionStart"
        private const val KEY_AUTHORITATIVE = "authoritative"
        private const val KEY_STATE = "state"
        private const val KEY_SINCE = "since"
        private const val KEY_READING = "lastReadingAt"
        private const val KEY_OUT = "outCount"
        private const val KEY_POCKET = "pocketMillis"
        private const val KEY_ACTIVE = "active"
        private const val KEY_ENDED = "endedAt"
    }
}
