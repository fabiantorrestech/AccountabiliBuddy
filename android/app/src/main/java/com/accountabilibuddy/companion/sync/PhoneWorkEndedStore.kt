package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.shared.sync.SyncWorkEnded
import com.accountabilibuddy.shared.sync.SyncWorkEndedWindow
import java.time.Instant
import java.time.LocalDate

/**
 * Work ended on this phone with "I've finished work for the day" (ADR 67), in the PC's `workEnded` shape: the plan date, the ended
 * windows and when they were last edited, times only. Kept in the private preferences "phoneWorkEnded" and written into this phone's
 * sync file; the PCs apply it after their own looser-change delay. Only today's is kept.
 */
class PhoneWorkEndedStore(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** Called after [end]: this phone's file is written and the guards look again. Set by the app. */
    var onChanged: () -> Unit = {}

    /** What this phone ended for [date]; null when nothing (or another date's). */
    fun forDate(date: LocalDate): SyncWorkEnded? = load()?.takeIf { it.date == date }

    /** The phone's latest record whatever its date, for its sync file (receivers keep only today's). */
    fun latest(): SyncWorkEnded? = load()

    /** Ends [windows] on [date] at [now], added to what was already ended that day. */
    fun end(date: LocalDate, windows: List<SyncWorkEndedWindow>, now: Instant) {
        val kept = forDate(date)?.windows.orEmpty()
        save(SyncWorkEnded(date, (kept + windows).distinct().sortedBy { it.start }, now))
        onChanged()
    }

    private fun save(ended: SyncWorkEnded) {
        prefs.edit()
            .putString(KEY_DATE, ended.date.toString())
            .putLong(KEY_EDITED, ended.editedAt.toEpochMilli())
            .putString(KEY_WINDOWS, ended.windows.joinToString(";") { "${it.start.toEpochMilli()}-${it.end.toEpochMilli()}" })
            .apply()
    }

    private fun load(): SyncWorkEnded? = runCatching {
        val date = LocalDate.parse(prefs.getString(KEY_DATE, null) ?: return null)
        val windows = prefs.getString(KEY_WINDOWS, "").orEmpty().split(';').filter { it.isNotBlank() }.mapNotNull { text ->
            val (start, end) = text.split('-').map { it.toLong() }
            SyncWorkEndedWindow(Instant.ofEpochMilli(start), Instant.ofEpochMilli(end)).takeIf { it.end.isAfter(it.start) }
        }
        SyncWorkEnded(date, windows, Instant.ofEpochMilli(prefs.getLong(KEY_EDITED, 0L)))
    }.getOrNull()

    companion object {
        const val PREFS = "phoneWorkEnded"
        private const val KEY_DATE = "date"
        private const val KEY_EDITED = "editedAt"
        private const val KEY_WINDOWS = "windows"
    }
}
