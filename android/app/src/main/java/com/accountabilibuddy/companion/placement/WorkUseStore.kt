package com.accountabilibuddy.companion.placement

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.time.Duration
import java.time.Instant

/**
 * "Need it for work" (phone placement): a use says what for (call, 2FA, work app, other), a short note and 5, 10 or
 * 15 minutes. Uses 1-3 in a session take a note of 5 characters; from the 4th, 20 characters and a 30-second wait
 * (like ending a session early). During a use the phone being out is not counted or acted on, and the sync file carries
 * its end time only. Notes and kinds stay in preferences "workUse" on this phone (15 days, then dropped).
 */
class WorkUseStore(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** Every use of the last 15 days, oldest first. */
    @Synchronized
    fun all(now: Instant = Instant.now()): List<WorkUse> {
        val array = runCatching { JSONArray(prefs.getString(KEY_USES, "[]")) }.getOrDefault(JSONArray())
        return (0 until array.length()).mapNotNull { index ->
            runCatching {
                val use = array.getJSONObject(index)
                WorkUse(
                    Instant.parse(use.getString("session")),
                    Instant.parse(use.getString("start")),
                    Instant.parse(use.getString("end")),
                    WorkUseKind.valueOf(use.getString("kind")),
                    use.optString("note"),
                )
            }.getOrNull()
        }.filter { it.start.isAfter(now.minus(KEPT)) }.sortedBy { it.start }
    }

    /** The uses so far in the session that started at [sessionStart]. */
    fun usesIn(sessionStart: Instant): Int = all().count { PlacementSession.same(it.sessionStart, sessionStart) }

    /** What the next use in that session takes. */
    fun termsFor(sessionStart: Instant): WorkUseTerms =
        if (usesIn(sessionStart) < FREE_USES) WorkUseTerms(SHORT_NOTE, 0) else WorkUseTerms(LONG_NOTE, WAIT_SECONDS)

    /**
     * Starts the wait for the next use when it has one (kept across the chooser being closed, so closing it is not a
     * way round the wait). Returns when the use may start.
     */
    @Synchronized
    fun begin(sessionStart: Instant, now: Instant = Instant.now()): Instant {
        val terms = termsFor(sessionStart)
        val kept = prefs.getLong(KEY_WAIT_FROM, 0L).takeIf { it > 0 }?.let(Instant::ofEpochMilli)
        val askedAt = prefs.getLong(KEY_WAIT_ASKED, 0L).takeIf { it > 0 }?.let(Instant::ofEpochMilli)
        if (kept != null && askedAt != null && Duration.between(askedAt, now) <= WAIT_KEPT) {
            return kept
        }

        val from = now.plusSeconds(terms.waitSeconds.toLong())
        prefs.edit().putLong(KEY_WAIT_FROM, from.toEpochMilli()).putLong(KEY_WAIT_ASKED, now.toEpochMilli()).apply()
        return from
    }

    /** Starts a use; false when the note is too short, the wait isn't over, or [minutes] isn't one of [LENGTHS]. */
    @Synchronized
    fun start(sessionStart: Instant, kind: WorkUseKind, note: String, minutes: Int, now: Instant = Instant.now()): Boolean {
        val terms = termsFor(sessionStart)
        val text = note.trim()
        if (minutes !in LENGTHS || text.length < terms.minNoteLength) {
            return false
        }

        if (terms.waitSeconds > 0) {
            val from = prefs.getLong(KEY_WAIT_FROM, 0L).takeIf { it > 0 }?.let(Instant::ofEpochMilli) ?: return false
            if (now.isBefore(from)) {
                return false
            }
        }

        val use = WorkUse(sessionStart, now, now.plus(Duration.ofMinutes(minutes.toLong())), kind, text)
        save(all(now) + use)
        prefs.edit().remove(KEY_WAIT_FROM).remove(KEY_WAIT_ASKED).apply()
        return true
    }

    /** The use under way, or null. */
    fun active(now: Instant = Instant.now()): WorkUse? = all(now).lastOrNull { !now.isBefore(it.start) && now.isBefore(it.end) }

    /** When the use under way ends; null when none runs. */
    fun activeUntil(now: Instant = Instant.now()): Instant? = active(now)?.end

    /** Done early: the use under way ends now. */
    @Synchronized
    fun endNow(now: Instant = Instant.now()) {
        val current = active(now) ?: return
        save(all(now).map { if (it == current) it.copy(end = now) else it })
    }

    /** The last use of the session that ended within [window] before [now] ("Done? Put it back"); null when none. */
    fun justEnded(sessionStart: Instant, now: Instant = Instant.now(), window: Duration = JUST_ENDED): WorkUse? =
        all(now).lastOrNull {
            PlacementSession.same(it.sessionStart, sessionStart) && !it.end.isAfter(now) && Duration.between(it.end, now) <= window
        }

    private fun save(uses: List<WorkUse>) {
        val array = JSONArray()
        uses.forEach { use ->
            array.put(
                JSONObject().put("session", use.sessionStart.toString()).put("start", use.start.toString())
                    .put("end", use.end.toString()).put("kind", use.kind.name).put("note", use.note),
            )
        }
        prefs.edit().putString(KEY_USES, array.toString()).apply()
    }

    companion object {
        /** The lengths a use can have, in minutes. */
        val LENGTHS = listOf(5, 10, 15)

        /** Uses in a session with the short terms; the 4th takes the long ones. */
        const val FREE_USES = 3
        const val SHORT_NOTE = 5
        const val LONG_NOTE = 20
        const val WAIT_SECONDS = 30
        private val KEPT: Duration = Duration.ofDays(15)
        private val WAIT_KEPT: Duration = Duration.ofMinutes(10)
        private val JUST_ENDED: Duration = Duration.ofMinutes(10)
        private const val PREFS = "workUse"
        private const val KEY_USES = "uses"
        private const val KEY_WAIT_FROM = "waitFrom"
        private const val KEY_WAIT_ASKED = "waitAsked"
    }
}
