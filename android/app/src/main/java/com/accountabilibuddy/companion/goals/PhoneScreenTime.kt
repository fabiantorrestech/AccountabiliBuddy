package com.accountabilibuddy.companion.goals

import android.content.Context
import android.os.Looper
import android.os.SystemClock
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalAppRow
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalScreenTimeDerived
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId

/**
 * The phone's screen time for goals (ST3): Android's own usage records (Usage access, no new permission) read per day into
 * [GoalAppRow]s, matched by the pure rules in [GoalScreenTimeDerived]. Read on the phone only: app names and packages stay here and are
 * never logged; only the per-day minutes of a goal become goal entries ([PhoneGoalDerived]).
 * - Closed days are read once per process and kept; today is read again at most every 5 minutes.
 * - Never read on the main thread (the usage query is a slow call) and never waits on the lock there: a caller on the main thread gets
 *   what was kept, at once.
 * - Without Usage access nothing is read, so nothing is written and nothing is removed.
 */
object PhoneScreenTime {
    private const val TODAY_EVERY_MS = 5 * 60 * 1000L

    private val lock = Any()
    private val closedDays = HashMap<LocalDate, List<GoalAppRow>>()
    private var todayRows: List<GoalAppRow> = emptyList()
    private var todayDate: LocalDate? = null
    private var todayReadAt = -TODAY_EVERY_MS
    @Volatile
    private var lastRowsByDay: Map<LocalDate, List<GoalAppRow>> = emptyMap()
    @Volatile
    private var reader: PhoneUsageReader? = null

    /** Whether Usage access is on. */
    fun hasAccess(context: Context): Boolean = reader(context).hasAccess()

    /**
     * The last [GoalScreenTimeDerived.DAYS] days' rows, as far as they could be read; days without records are left out. [force] reads
     * today again now. Empty without access.
     */
    fun rowsByDay(context: Context, force: Boolean = false): Map<LocalDate, List<GoalAppRow>> {
        // Before the lock: the main thread never waits for a usage read that another thread is doing.
        if (Looper.myLooper() == Looper.getMainLooper()) return lastRowsByDay
        synchronized(lock) { return readAll(context, force) }
    }

    private fun readAll(context: Context, force: Boolean): Map<LocalDate, List<GoalAppRow>> {
        val usage = reader(context)
        if (!usage.hasAccess()) {
            closedDays.clear()
            todayRows = emptyList()
            lastRowsByDay = emptyMap()
            return lastRowsByDay
        }

        val zone = ZoneId.systemDefault()
        val today = LocalDate.ofInstant(TrustedTime.now(), zone)
        // The usage records are stamped by the system clock; a day is closed (cached) only when that clock says it is over too.
        val systemToday = LocalDate.now(zone)
        val result = LinkedHashMap<LocalDate, List<GoalAppRow>>()
        for (back in GoalScreenTimeDerived.DAYS - 1 downTo 1) {
            val day = today.minusDays(back.toLong())
            val rows = closedDays[day] ?: read(usage, day).also { if (day < systemToday) closedDays[day] = it }
            if (rows.isNotEmpty()) result[day] = rows
        }

        val now = SystemClock.elapsedRealtime()
        if (force || todayDate != today || now - todayReadAt >= TODAY_EVERY_MS) {
            todayRows = read(usage, today)
            todayDate = today
            todayReadAt = now
        }

        if (todayRows.isNotEmpty()) result[today] = todayRows
        closedDays.keys.removeAll { it < today.minusDays(GoalScreenTimeDerived.DAYS.toLong()) }
        lastRowsByDay = result
        return result
    }

    /** What the pick list offers from the last week: (package, shown name) of apps used, most time first. Empty without access. */
    fun seenApps(context: Context): List<Pair<String, String>> {
        val totals = LinkedHashMap<String, Pair<String, Double>>()
        for (rows in rowsByDay(context).values) {
            for (row in rows) {
                val before = totals[row.id]?.second ?: 0.0
                totals[row.id] = (row.label ?: row.id) to (before + row.minutes)
            }
        }

        return totals.entries.sortedByDescending { it.value.second }.map { it.key to it.value.first }
    }

    /** In-memory entries for the goals' days this phone holds no entry for yet, from what was kept (never reads on the main thread). */
    fun unstored(app: CompanionApp, goals: List<Goal>, stored: List<GoalEntry>): List<GoalEntry> {
        val rows = lastRowsByDay
        if (rows.isEmpty()) return emptyList()
        return GoalScreenTimeDerived.unstored(goals, app.sync.deviceId, rows, stored, OffsetDateTime.now())
    }

    private fun read(usage: PhoneUsageReader, day: LocalDate): List<GoalAppRow> {
        val window = LocalWindow(day.atStartOfDay(), day.plusDays(1).atStartOfDay())
        return usage.read(window, emptyList()).apps.map { GoalAppRow(it.packageName, it.label, null, it.total.seconds / 60.0) }
    }

    private fun reader(context: Context): PhoneUsageReader = reader ?: synchronized(lock) {
        reader ?: PhoneUsageReader(context.applicationContext).also { reader = it }
    }
}
