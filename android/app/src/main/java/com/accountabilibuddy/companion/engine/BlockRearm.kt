package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.BlockLift
import com.accountabilibuddy.companion.model.BlockRef
import com.accountabilibuddy.companion.model.BlockRules
import com.accountabilibuddy.companion.model.DayPlan
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime

/**
 * When blocks go off or come back on (ADR 37), from the day plan's block rules for one device: a day's re-enable time
 * (anything guarded back on) and each lift window's start (Auto: off) and end (back on). A port of the PC's Core
 * BlockRearm (same rules, kept in step by hand).
 */
object BlockRearm {
    /** At [at], turn [blocks] on or off (null = every guarded block of the device); a heads-up at [warnAt] (null = none). */
    data class Event(
        val at: LocalDateTime,
        val turnOn: Boolean,
        val blocks: List<BlockRef>?,
        val warnAt: LocalDateTime?,
        val why: String,
        /** For "every guarded block" ([blocks] null): blocks left out (they have their own time). */
        val except: List<BlockRef>? = null,
    )

    fun forDay(plan: DayPlan, day: LocalDate, device: String): List<Event> {
        val rules = plan.templateFor(day).rules
        val start = day.atStartOfDay()
        val events = mutableListOf<Event>()

        val deviceTime = rules.reenableFor(device)
        val reenable = if (deviceTime == LocalTime.MIDNIGHT) start.plusDays(1) else day.atTime(deviceTime)
        val own = rules.blockTimes?.filter { it.block.device == device }.orEmpty()
        val guarded = rules.guarded?.filter { b -> b.device == device && own.none { it.block.same(b) } }
        if (guarded == null || guarded.isNotEmpty()) {
            val except = if (guarded == null && own.isNotEmpty()) own.map { it.block } else null
            events += Event(reenable, true, guarded, warn(reenable, rules), "the day's re-enable time", except)
        }
        // Blocks with their own time (schema v11).
        own.forEach { b ->
            val at = if (b.at == LocalTime.MIDNIGHT) start.plusDays(1) else day.atTime(b.at)
            events += Event(at, true, listOf(b.block), warn(at, rules), "its own re-enable time")
        }

        for (lift in rules.lifts) {
            val mine = lift.blocks.filter { it.device == device }
            if (mine.isEmpty()) continue
            val (from, to) = window(day, lift)
            if (lift.mode == BlockLift.AUTO) events += Event(from, false, mine, null, "a lift window starts")
            events += Event(to, true, mine, warn(to, rules), "a lift window ends")
        }
        return events.sortedBy { it.at }
    }

    /** A block on this device as its blocker says now. */
    data class Current(val ref: BlockRef, val on: Boolean, val locked: Boolean = false)

    /**
     * What to change after [events] (in time order): each block's last event decides whether it should be on; an
     * event for "every guarded block" covers the blocks in [known] (seen on before). Locked blocks and blocks already
     * as wanted are left out.
     */
    fun toApply(events: List<Event>, current: List<Current>, known: Set<String>): List<Pair<Current, Boolean>> {
        val wanted = linkedMapOf<String, Boolean>()
        for (event in events) {
            targets(event, current, known).forEach { wanted[key(it.ref)] = event.turnOn }
        }
        return current.mapNotNull { block ->
            val on = wanted[key(block.ref)] ?: return@mapNotNull null
            if (block.locked || block.on == on) null else block to on
        }
    }

    /** The blocks [event] covers on this device. */
    fun targets(event: Event, current: List<Current>, known: Set<String>): List<Current> =
        event.blocks?.let { refs -> current.filter { block -> refs.any { it.same(block.ref) } } }
            ?: current.filter { key(it.ref) in known && event.except?.none { e -> e.same(it.ref) } != false }

    /** A block's key for [known]: "kind/id". */
    fun key(ref: BlockRef): String = "${ref.kind}/${ref.id}"

    /** The events of the days around that fall in ([after], [until]]. */
    fun between(plan: DayPlan, device: String, after: LocalDateTime, until: LocalDateTime): List<Event> =
        around(plan, device, after, until).filter { it.at > after && it.at <= until }.sortedBy { it.at }

    /** The heads-ups due in ([after], [until]]. */
    fun warningsBetween(plan: DayPlan, device: String, after: LocalDateTime, until: LocalDateTime): List<Event> =
        around(plan, device, after, until.plusDays(1))
            .filter { e -> e.warnAt?.let { it > after && it <= until } == true }
            .sortedBy { it.warnAt }

    /** The next event after [now] (to set the alarm for), or null. */
    fun next(plan: DayPlan, device: String, now: LocalDateTime): LocalDateTime? =
        around(plan, device, now, now.plusDays(2))
            .flatMap { listOfNotNull(it.at, it.warnAt) }
            .filter { it > now }
            .minOrNull()

    /** True when [block] is in one of its lift windows at [now]. */
    fun inLift(plan: DayPlan, block: BlockRef, now: LocalDateTime): Boolean =
        listOf(now.toLocalDate().minusDays(1), now.toLocalDate()).any { day ->
            plan.templateFor(day).rules.lifts.filter { lift -> lift.blocks.any { it.same(block) } }.any { lift ->
                val (from, to) = window(day, lift)
                now >= from && now < to
            }
        }

    private fun around(plan: DayPlan, device: String, after: LocalDateTime, until: LocalDateTime): List<Event> {
        val events = mutableListOf<Event>()
        var day = after.toLocalDate().minusDays(1)
        while (!day.isAfter(until.toLocalDate())) {
            events += forDay(plan, day, device)
            day = day.plusDays(1)
        }
        return events
    }

    private fun window(day: LocalDate, lift: BlockLift): Pair<LocalDateTime, LocalDateTime> {
        val from = day.atTime(lift.start)
        val to = day.atTime(lift.end)
        return from to if (!to.isAfter(from)) to.plusDays(1) else to
    }

    private fun warn(at: LocalDateTime, rules: BlockRules): LocalDateTime? =
        rules.warnMinutes?.takeIf { it > 0 }?.let { at.minusMinutes(it.toLong()) }
}
