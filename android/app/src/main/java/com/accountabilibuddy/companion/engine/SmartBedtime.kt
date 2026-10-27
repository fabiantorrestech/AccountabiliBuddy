package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.LocalWindow
import java.time.DayOfWeek
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime

/** Which nights a smart bedtime is for: before a workday, or before an Off-Day. */
enum class NightKind { Workday, OffDay }

/**
 * One measured night (ADR 34), named by its evening.
 * @property asleep when the first sleep of the night began (Health Connect).
 * @property woke when the last sleep of the night ended.
 * @property phoneDown the phone's last screen-off before falling asleep (Usage access), within 3 hours; null when unknown.
 * @property asked the bedtime the app asked for that night; null when the smart bedtime wasn't on.
 */
data class NightRecord(
    val evening: LocalDate,
    val kind: NightKind,
    val asleep: LocalDateTime,
    val woke: LocalDateTime,
    val sleptMinutes: Int,
    val phoneDown: LocalDateTime? = null,
    val asked: LocalDateTime? = null,
)

/**
 * A template's learned time in bed before waking.
 * @property levelMinutes null while learning (fewer than [SmartBedtime.NIGHTS_TO_START] nights).
 * @property startMinutes where it started: it never steps back below this.
 * @property missedWeeks weeks in a row made on under half the nights.
 * @property lastStep the Monday it was last weighed.
 */
data class SmartLevel(val levelMinutes: Int? = null, val startMinutes: Int? = null, val missedWeeks: Int = 0, val lastStep: LocalDate? = null)

/**
 * The smart bedtime (ADR 34): learns how long the user really spends in bed before waking, per kind of night, and
 * moves it 15 minutes earlier after each week made on most nights, toward the sleep goal plus the usual time to fall
 * asleep. Tonight's bedtime is tomorrow's wake time minus the level. Pure: the phone feeds it nights and saves what
 * it returns.
 */
object SmartBedtime {
    const val NIGHTS_TO_START = 5
    const val STEP_MINUTES = 15
    private const val GRACE_MINUTES = 10L
    private const val DEFAULT_LATENCY = 15
    private val PHONE_DOWN_WINDOW: Duration = Duration.ofHours(3)

    /** The kind of night leading into [day]: [isWorkday] says by the plan's week map (A or B, custom templates), as the PC does. */
    fun kindOf(day: LocalDate, isWorkday: (LocalDate) -> Boolean): NightKind = if (isWorkday(day)) NightKind.Workday else NightKind.OffDay

    /** The night a sleep belongs to: the evening 12 hours before it ended (6:45 AM on the 24th is the night of the 23rd). */
    fun eveningOf(end: LocalDateTime): LocalDate = end.minusHours(12).toLocalDate()

    /**
     * Measured sleep grouped into nights that ended before [now] (sessions overlapping count once). [phoneDown] finds the
     * last screen-off in a window before an evening's sleep, [asked] the bedtime asked for an evening.
     */
    fun nights(
        slept: List<LocalWindow>,
        now: LocalDateTime,
        isWorkday: (LocalDate) -> Boolean,
        phoneDown: (LocalDate, LocalWindow) -> LocalDateTime?,
        asked: (LocalDate) -> LocalDateTime?,
    ): List<NightRecord> = slept.filter { it.end.isAfter(it.start) && !it.end.isAfter(now) }
        .groupBy { eveningOf(it.end) }
        // A night counts once its sleep ended an hour ago (a short wake-up in the night isn't the morning yet).
        .filterValues { windows -> !now.isBefore(windows.maxOf { it.end }.plusHours(1)) }
        .map { (evening, windows) ->
            val asleep = windows.minOf { it.start }
            NightRecord(
                evening = evening,
                kind = kindOf(evening.plusDays(1), isWorkday),
                asleep = asleep,
                woke = windows.maxOf { it.end },
                sleptMinutes = DayTimeline.union(windows).sumOf { Duration.between(it.start, it.end).toMinutes() }.toInt(),
                phoneDown = phoneDown(evening, LocalWindow(asleep.minus(PHONE_DOWN_WINDOW), asleep)),
                asked = asked(evening),
            )
        }
        .sortedBy { it.evening }

    /** The usual time from putting the phone down to falling asleep: 5 to 60 minutes, 15 until 3 nights show it. */
    fun latencyMinutes(nights: List<NightRecord>): Int {
        val gaps = nights.mapNotNull { night -> night.phoneDown?.let { Duration.between(it, night.asleep).toMinutes() } }
            .filter { it in 0..PHONE_DOWN_WINDOW.toMinutes() }
        return if (gaps.size < 3) DEFAULT_LATENCY else median(gaps).toInt().coerceIn(5, 60)
    }

    /** Made it: the phone down by the asked bedtime (10 minutes' grace), or without phone data asleep in time. Null when nothing was asked. */
    fun madeIt(night: NightRecord, latencyMinutes: Int): Boolean? {
        val asked = night.asked ?: return null
        val phoneDown = night.phoneDown
        return if (phoneDown != null) !phoneDown.isAfter(asked.plusMinutes(GRACE_MINUTES))
        else !night.asleep.isAfter(asked.plusMinutes(latencyMinutes + GRACE_MINUTES))
    }

    /** Where learning starts: the median real time in bed before waking, at most the goal plus the time to fall asleep. Null with too few nights. */
    fun startMinutes(nights: List<NightRecord>, latencyMinutes: Int, goal: Duration): Int? {
        if (nights.size < NIGHTS_TO_START) return null
        val inBed = nights.map { night ->
            val down = night.phoneDown ?: night.asleep.minusMinutes(latencyMinutes.toLong())
            Duration.between(down, night.woke).toMinutes()
        }
        return median(inBed).toInt().coerceAtMost(cap(goal, latencyMinutes))
    }

    /**
     * Brings [level] up to date on [today]: starts it once there are enough nights of [kind]; on a Monday weighs the
     * week's nights (evenings of the 7 days before): made on 80% or more, +15 minutes up to the cap; under half two
     * weeks running, −15 minutes, never below the start.
     */
    fun update(level: SmartLevel, kind: NightKind, nights: List<NightRecord>, today: LocalDate, goal: Duration): SmartLevel {
        val own = nights.filter { it.kind == kind && !it.evening.isBefore(today.minusDays(21)) }
        val latency = latencyMinutes(own)
        if (level.levelMinutes == null) {
            val start = startMinutes(own, latency, goal) ?: return level
            return SmartLevel(start, start, 0, today)
        }

        if (today.dayOfWeek != DayOfWeek.MONDAY || level.lastStep == today) return level
        val week = own.filter { !it.evening.isBefore(today.minusDays(7)) && it.evening.isBefore(today) }
            .mapNotNull { madeIt(it, latency) }
        if (week.isEmpty()) return level.copy(lastStep = today)

        val share = week.count { it }.toDouble() / week.size
        val current = level.levelMinutes
        return when {
            share >= 0.8 -> level.copy(levelMinutes = (current + STEP_MINUTES).coerceAtMost(cap(goal, latency)), missedWeeks = 0, lastStep = today)
            share < 0.5 && level.missedWeeks + 1 >= 2 ->
                level.copy(levelMinutes = (current - STEP_MINUTES).coerceAtLeast(level.startMinutes ?: current - STEP_MINUTES), missedWeeks = 0, lastStep = today)
            share < 0.5 -> level.copy(missedWeeks = level.missedWeeks + 1, lastStep = today)
            else -> level.copy(missedWeeks = 0, lastStep = today)
        }
    }

    /** Tonight's bedtime: [wake] minus the level. */
    fun bedtime(levelMinutes: Int, wake: LocalDateTime): LocalDateTime = wake.minusMinutes(levelMinutes.toLong())

    /** The most time in bed it asks for: the goal plus the usual time to fall asleep. */
    fun cap(goal: Duration, latencyMinutes: Int): Int = (goal.toMinutes() + latencyMinutes).toInt()

    /** What the status line says about a kind of night (averages over its last 14 nights). */
    data class Summary(val nights: Int, val latencyMinutes: Int, val averageAsleep: java.time.LocalTime?, val averageSleptMinutes: Int?, val madeThisWeek: Int, val askedThisWeek: Int)

    fun summary(nights: List<NightRecord>, kind: NightKind, today: LocalDate): Summary {
        val own = nights.filter { it.kind == kind && !it.evening.isBefore(today.minusDays(14)) }
        val latency = latencyMinutes(own)
        // The time of night as minutes from noon, so 11:30 PM and 12:30 AM average to midnight.
        val fromNoon = own.map { Duration.between(it.asleep.toLocalDate().atTime(12, 0).let { noon -> if (it.asleep.isBefore(noon)) noon.minusDays(1) else noon }, it.asleep).toMinutes() }
        val monday = today.with(DayOfWeek.MONDAY)
        val week = own.filter { !it.evening.isBefore(monday.minusDays(1)) }.mapNotNull { madeIt(it, latency) }
        return Summary(
            nights = own.size,
            latencyMinutes = latency,
            averageAsleep = fromNoon.takeIf { it.isNotEmpty() }?.let { java.time.LocalTime.NOON.plusMinutes(it.average().toLong()) },
            averageSleptMinutes = own.takeIf { it.isNotEmpty() }?.map { it.sleptMinutes }?.average()?.toInt(),
            madeThisWeek = week.count { it },
            askedThisWeek = week.size,
        )
    }

    private fun median(values: List<Long>): Long {
        val sorted = values.sorted()
        val middle = sorted.size / 2
        return if (sorted.size % 2 == 1) sorted[middle] else (sorted[middle - 1] + sorted[middle]) / 2
    }
}
