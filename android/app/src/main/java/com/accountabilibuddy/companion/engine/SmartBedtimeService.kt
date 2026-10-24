package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime

/**
 * Runs the smart bedtime (ADR 34) on the phone's refresh: records the nights measured since (Health Connect sleep the
 * app read, the phone's last screen-off before each), brings each switched-on kind's level up to date (Mondays step),
 * hands the levels to the sleep schedule and remembers tonight's bedtime as asked. Status lines for Settings.
 */
class SmartBedtimeService(private val app: CompanionApp) {
    val store = SmartBedtimeStore(app)
    private val usage = PhoneUsageReader(app)

    @Volatile
    private var lastLearned: LocalDateTime? = null

    // The switches and limits are part of the shared sleep settings (ADR 35).
    private fun sleep() = app.store.state.value.sleep

    /** The levels in use now, by kind (only kinds switched on and past learning). */
    fun levelsInUse(): Map<NightKind, Int> = NightKind.entries
        .filter { sleep().smartOn(it) }
        .mapNotNull { kind -> store.level(kind).levelMinutes?.let { kind to it } }
        .toMap()

    fun refresh(now: LocalDateTime = LocalDateTime.now()) {
        val state = app.store.state.value
        val plan = state.document.dayPlan
        if (NightKind.entries.none { sleep().smartOn(it) }) {
            app.store.setSmartLevels(emptyMap())
            return
        }

        // Learning at most every 15 minutes (it reads Usage access); handing the levels over every time.
        val last = lastLearned
        if (last == null || Duration.between(last, now) >= Duration.ofMinutes(15)) {
            lastLearned = now
            learn(now)
        }

        val levels = levelsInUse()
        app.store.setSmartLevels(levels)

        // Tonight's bedtime, remembered as asked until it has passed (then that night's is fixed).
        val night = app.store.state.value.sleep.toSchedule(plan).nightAround(now)
        val evening = SmartBedtime.eveningOf(night.end)
        if (now.isBefore(night.start) && SmartBedtime.kindOf(evening.plusDays(1), plan.workdays) in levels) {
            store.saveAsked(evening, night.start)
        }
    }

    private fun learn(now: LocalDateTime) {
        val state = app.store.state.value
        val plan = state.document.dayPlan
        val known = store.nights().associateBy { it.evening }
        val measured = SmartBedtime.nights(
            state.slept, now, plan.workdays,
            phoneDown = { evening, window -> known[evening]?.phoneDown ?: runCatching { usage.lastScreenOff(window) }.getOrNull() },
            asked = { evening -> store.asked(evening) },
        )
        val nights = (known + measured.associateBy { it.evening }).values.toList()
        store.saveNights(nights, now.toLocalDate())

        val goal = goal()
        for (kind in NightKind.entries.filter { sleep().smartOn(it) }) {
            store.saveLevel(kind, SmartBedtime.update(store.level(kind), kind, nights, now.toLocalDate(), goal))
        }
    }

    /** The goal it grows toward: the sleep goal, never past the maximum. */
    private fun goal(): Duration {
        val goal = app.store.state.value.sleep.goal
        val max = Duration.ofMinutes((sleep().maxHours * 60).toLong())
        return if (goal > max) max else goal
    }

    /** "7 h 15 m in bed → tonight 11:30 PM · last 2 weeks asleep 11:48 PM avg, 6 h 52 m · this week 3 of 4 · next step Monday". */
    fun status(kind: NightKind, clock: ClockFormat, now: LocalDateTime = LocalDateTime.now()): String {
        if (!sleep().smartOn(kind)) return "Off"
        val today = now.toLocalDate()
        val nights = store.nights()
        val summary = SmartBedtime.summary(nights, kind, today)
        val level = store.level(kind).levelMinutes
        val parts = mutableListOf<String>()
        if (level == null) {
            val have = nights.count { it.kind == kind && !it.evening.isBefore(today.minusDays(21)) }
            parts += "Learning: $have of ${SmartBedtime.NIGHTS_TO_START} nights (needs Health Connect sleep)"
        } else {
            val plan = app.store.state.value.document.dayPlan
            val tonight = nextNight(kind, now, plan.workdays)
            val bed = app.store.state.value.sleep.toSchedule(plan).night(tonight).start
            parts += "${DayBarBuilder.duration(Duration.ofMinutes(level.toLong()))} in bed → ${if (tonight == today || tonight == today.minusDays(1)) "tonight" else tonight.dayOfWeek.name.lowercase().replaceFirstChar { it.uppercase() }} ${clock.time(bed)}"
            val minimum = (sleep().minHours * 60).toInt() + summary.latencyMinutes
            parts += if (level >= minimum) "minimum reached, growing toward ${DayBarBuilder.duration(goal())}" else "growing toward your ${DayBarBuilder.duration(Duration.ofMinutes((sleep().minHours * 60).toLong()))} minimum"
        }
        summary.averageAsleep?.let { asleep ->
            parts += "last 2 weeks asleep ${clock.time(today.atTime(asleep))} avg" + (summary.averageSleptMinutes?.let { ", ${DayBarBuilder.duration(Duration.ofMinutes(it.toLong()))}" } ?: "")
        }
        if (summary.askedThisWeek > 0) parts += "this week ${summary.madeThisWeek} of ${summary.askedThisWeek}"
        if (level != null) parts += "next step Monday"
        return parts.joinToString(" · ")
    }

    // The next evening whose night is of this kind (tonight when it is).
    private fun nextNight(kind: NightKind, now: LocalDateTime, workdays: Set<java.time.DayOfWeek>): LocalDate {
        var evening = if (now.hour < 12) now.toLocalDate().minusDays(1) else now.toLocalDate()
        repeat(7) {
            if (SmartBedtime.kindOf(evening.plusDays(1), workdays) == kind) return evening
            evening = evening.plusDays(1)
        }
        return evening
    }

    /** What the PCs get in the sync file (ADR 34): per kind, the level and the status numbers; null when it's off. */
    fun forSync(now: LocalDateTime = LocalDateTime.now()): Map<NightKind, SmartShare>? {
        val kinds = NightKind.entries.filter { sleep().smartOn(it) }
        if (kinds.isEmpty()) return null
        val nights = store.nights()
        return kinds.associateWith { kind ->
            val summary = SmartBedtime.summary(nights, kind, now.toLocalDate())
            SmartShare(store.level(kind).levelMinutes, summary)
        }
    }

    /** One kind's share for the PCs. */
    data class SmartShare(val levelMinutes: Int?, val summary: SmartBedtime.Summary)

    /** [forSync] in the sync file's shape. */
    fun forSyncFile(now: LocalDateTime = LocalDateTime.now()): com.accountabilibuddy.shared.sync.SyncSmartBedtime? {
        val shares = forSync(now) ?: return null
        fun level(kind: NightKind) = shares[kind]?.let { share ->
            com.accountabilibuddy.shared.sync.SyncSmartLevel(
                share.levelMinutes, share.summary.nights, share.summary.latencyMinutes,
                share.summary.averageAsleep?.let { "%02d:%02d".format(it.hour, it.minute) }, share.summary.averageSleptMinutes,
                share.summary.madeThisWeek, share.summary.askedThisWeek,
            )
        }
        return com.accountabilibuddy.shared.sync.SyncSmartBedtime(level(NightKind.Workday), level(NightKind.OffDay))
    }
}
