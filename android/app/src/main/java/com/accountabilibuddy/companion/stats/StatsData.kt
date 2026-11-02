package com.accountabilibuddy.companion.stats

import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncFile
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Everything the focus views draw from, read once per refresh from the files received from the user's devices (the PC's
 * StatsData): the counted focus for the days the phone keeps, the drift visits and the sessions of each day. Nothing here is
 * new data, only what the sync files already carry (ADR 22); window titles and addresses never reach the phone.
 *
 * @property firstDay The oldest date the phone keeps (90 days, [com.accountabilibuddy.shared.sync.SyncMerge.KEPT_FOR]).
 * @property firstData The first date with any session on any device; earlier dates have no data at all, never zeros. Today when none.
 * @property hasHistory Some device sent at least one session.
 * @property goal The PC's daily deep work goal (ADR 29); null until a PC file has sent one.
 */
class StatsData(
    val today: LocalDate,
    val nowLocal: LocalDateTime,
    val zone: ZoneId,
    val firstDay: LocalDate,
    val firstData: LocalDate,
    val hasHistory: Boolean,
    val goal: Duration?,
    val windows: List<LocalWindow>,
    val days: List<StatsDay>,
    val drift: List<DriftVisit>,
    val sessions: List<StatsSession>,
    val isWorkday: (LocalDate) -> Boolean,
) {
    /** The local date a moment falls on. */
    fun dateOf(time: Instant): LocalDate = LocalDateTime.ofInstant(time, zone).toLocalDate()

    /** The day's entry, or null for a date before the first one with history (or after today). */
    fun dayOf(date: LocalDate): StatsDay? {
        val index = (date.toEpochDay() - firstDay.toEpochDay()).toInt()
        return if (!date.isBefore(firstData) && index >= 0 && index < days.size) days[index] else null
    }

    /** Every day from the first with history up to today, oldest first. */
    val historyDays: List<StatsDay> get() = days.filter { !it.date.isBefore(firstData) }

    /** The days from [from] to [to] that have happened and that have history. */
    fun daysBetween(from: LocalDate, to: LocalDate): List<StatsDay> =
        days.filter { !it.date.isBefore(from) && !it.date.isAfter(to) && !it.date.isAfter(today) && !it.date.isBefore(firstData) }

    /** The drift visits that began on the dates [from] to [to]. */
    fun driftBetween(from: LocalDate, to: LocalDate): List<DriftVisit> =
        drift.filter { dateOf(it.start).let { date -> !date.isBefore(from) && !date.isAfter(to) } }

    /** Drift time per date. */
    fun driftPerDay(): Map<LocalDate, Duration> =
        drift.groupBy { dateOf(it.start) }.mapValues { (_, visits) -> visits.fold(Duration.ZERO) { sum, visit -> sum.plus(visit.length) } }

    companion object {
        /** How many days the phone keeps received sessions. */
        const val KEPT_DAYS = 90

        /**
         * @param files Each received device's merged sync file, removed devices' kept copies included (as the PC's totals count them).
         * @param isWorkday Whether a date is a workday in the day plan (`DayPlan.isWorkday`).
         */
        fun build(files: List<SyncFile>, now: Instant, zone: ZoneId, isWorkday: (LocalDate) -> Boolean): StatsData {
            val nowLocal = LocalDateTime.ofInstant(now, zone)
            val today = nowLocal.toLocalDate()
            val first = today.minusDays(KEPT_DAYS - 1L)
            val windows = FocusWindows.of(files, zone)
            val days = FocusWindows.daysIn(windows, first, today)
            val sessions = files.flatMap { file ->
                val written = file.device.writtenAt
                file.sessions
                    .filter { it.end != null || !it.start.isAfter(written) }
                    .map { session ->
                        StatsSession(
                            file.device.name,
                            FocusWindows.local(session.start, zone),
                            FocusWindows.local(session.end ?: written, zone),
                            if (session.added) "Added by hand" else session.profile ?: "Focus session",
                        )
                    }
            }.filter { it.end.isAfter(it.start) }.sortedBy { it.start }
            val earliest = sessions.minOfOrNull { it.start.toLocalDate() }
            val firstData = maxOf(first, earliest ?: today)
            return StatsData(
                today, nowLocal, zone, first, firstData, earliest != null, goalOf(files), windows, days,
                DriftMarks.of(files.flatMap { it.drift }), sessions, isWorkday,
            )
        }

        // ADR 29: the newest PC file's goal, as the Today numbers read it.
        private fun goalOf(files: List<SyncFile>): Duration? = files
            .filter { it.device.kind == SyncDeviceCard.KIND_PC && (it.goalMinutes ?: 0) > 0 }
            .maxByOrNull { it.device.writtenAt }?.goalMinutes?.let { Duration.ofMinutes(it.toLong()) }
    }
}
