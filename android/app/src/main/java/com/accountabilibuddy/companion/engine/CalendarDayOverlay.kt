package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.FirstKnownEntry
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.OneOffDay
import com.accountabilibuddy.companion.model.PlanPart
import com.accountabilibuddy.companion.model.PlannedPart
import com.accountabilibuddy.companion.model.SleepTimes
import java.security.MessageDigest
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.UUID

/**
 * Everything [CalendarDayOverlay] needs besides the stored plan (ADR 58).
 *
 * @property events The classified events: from this phone's own calendar rules, or carried by a PC's sync file (already filtered to
 * files that are fresh enough; see [com.accountabilibuddy.companion.calendar.CalendarEventSources]).
 * @property firstKnown When each classified event was first seen here at its current time and category.
 * @property now The phone's clock.
 * @property zone The time zone event times are in.
 * @property sessionRunning A focus session is running: nothing loosens then.
 * @property looseningDelay How long before it starts an event must be known to loosen the day.
 * @property nightFallback The night before a date from the sleep settings, as [DayWorkWindows] takes it.
 * @property countedFocus The stretches of counted focus (a Budget counts them); a Work event's Budget credit leaves them out so no time counts twice. Null = none.
 */
data class CalendarOverlayInput(
    val events: List<ClassifiedEvent>,
    val firstKnown: List<FirstKnownEntry>,
    val now: Instant,
    val zone: ZoneId,
    val sessionRunning: Boolean,
    val looseningDelay: Duration,
    val nightFallback: ((LocalDate) -> SleepTimes?)? = null,
    val countedFocus: List<LocalWindow>? = null,
)

/**
 * Lays classified calendar events over the stored day plan and returns the plan the guards read (ADR 58): the effective plan, which
 * exists only in memory. A port of the PC's CalendarDayOverlay (same rules, kept in step by hand). It is the stored plan with a
 * one-off day for each date an event touches (that date's own template, cut where an event takes over, plus the event's part, marked
 * [PlanPart.fromEvent]), so [DayWorkWindows] and everything that reads it see events unchanged. Nothing here is saved: the stored plan,
 * its version hash and the sync file never see an event part.
 * - Strictness: Watch 3, Comms 2, Rest 1, nothing 0. An event is laid over the stored plan piece by piece along what the guards see
 *   there ([DayWorkWindows.partsIn], so a Budget day's stretch counts as Watch and a Free day as nothing). Where parts overlap, the
 *   strictest is the base. A piece that is the same or stricter applies at once. A looser one applies only when the phone first saw the
 *   event at its current start, end, category and behaviour at least the looser-change delay before it starts, and no session is
 *   running; an event carried by a PC's file also needs the PC's own notice ([ClassifiedEvent.carriedKnownSince]); otherwise the plan
 *   stands there. A Comms piece outside the stored Comms parts of a day that has some is looser, because it opens mail and chat. Where
 *   events overlap, the stricter wins.
 * - A Sleep or Pre-Work part is never cut: the piece of an event over one is dropped.
 * - An event that crosses midnight splits per date. An existing one-off day is the base for its date.
 * - Budget day: a Work event inside a Budget day's stretch never becomes a part. Under the looser rule it gives that day (the one whose
 *   stretch holds the time) a [DayTemplate.budgetCredit] for its time that has already passed, less any time also counted as focus, so a
 *   meeting frees nothing before it happens and nothing counts twice. Without notice it gives no credit but still wins overlaps.
 * - Free day: a Work event adds a window; [DayWorkWindows] keeps the parts flagged [PlanPart.fromEvent] when it strips a Free or Budget
 *   day's work parts.
 * With nothing classified, or nothing that applies, the stored plan itself is returned.
 */
object CalendarDayOverlay {
    private val OLDEST_EVENT: Duration = Duration.ofDays(8)

    /** A stamp this far ahead of the clock means the clock was set back since: it counts for nothing. */
    private val CLOCK_SLACK: Duration = Duration.ofMinutes(1)

    private val EARLIEST: LocalDateTime = LocalDateTime.of(1, 1, 1, 0, 0)

    fun apply(stored: DayPlan, input: CalendarOverlayInput): DayPlan {
        // Events that ended more than 8 days ago are left out: no guard looks back that far and a calendar keeps months of them.
        val localNow = LocalDateTime.ofInstant(input.now, input.zone)
        val oldest = localNow.minus(OLDEST_EVENT)
        val classified = input.events.filter { !it.event.localEnd.isBefore(oldest) }
        return if (classified.isEmpty()) stored else Overlay(stored, input, localNow).build(classified)
    }

    // KeepBase: an event the plan already agrees with (kept so it still wins an overlap); Part: the event's own part;
    // BudgetCredit: a Work event's time counted toward a Budget day.
    private enum class RegionKind { KeepBase, Part, BudgetCredit }

    // budgetDate and credited are for BudgetCredit regions: the Budget day whose stretch holds the time, and whether the notice was served.
    private data class Region(
        val window: LocalWindow,
        val kind: RegionKind,
        val category: DayCategory,
        val event: CalendarEvent,
        val strictness: Int,
        val budgetDate: LocalDate? = null,
        val credited: Boolean = false,
    )

    private class Overlay(private val stored: DayPlan, private val input: CalendarOverlayInput, private val localNow: LocalDateTime) {
        private val known: Map<Pair<String, String>, FirstKnownEntry> = FirstKnownTimes.byEvent(input.firstKnown)
        private val modes = HashMap<LocalDate, com.accountabilibuddy.companion.model.DayMode>()
        private val storedComms = HashMap<LocalDate, Boolean>()
        private val parts = HashMap<LocalDate, MutableList<PlanPart>>()
        private val changed = HashSet<LocalDate>()

        fun build(classified: List<ClassifiedEvent>): DayPlan {
            val regions = mutableListOf<Region>()
            for (item in classified) collect(item, regions)

            val placed = stricterWins(regions)
            placed.filter { it.kind == RegionKind.Part }.forEach(::placeEventPart)

            val credits = creditByDay(placed)
            if (changed.isEmpty() && credits.isEmpty()) return stored

            val touched = (changed + credits.keys).toSortedSet()
            val oneOffDays = stored.oneOffDays.filter { it.date !in touched }.toMutableList()
            for (date in touched) {
                val template = stored.templateFor(date)
                val existing = stored.oneOffDays.firstOrNull { it.date == date }
                oneOffDays += OneOffDay(
                    existing?.id ?: stableUuid("one-off|${date.format(DATE)}"),
                    date,
                    template.copy(parts = partsOf(date), workWindow = null, budgetCredit = credits[date] ?: Duration.ZERO),
                    existing?.note,
                )
            }

            return stored.copy(oneOffDays = oneOffDays.sortedBy { it.date })
        }

        // The regions one event takes, piece by piece along what the guards see under it.
        private fun collect(item: ClassifiedEvent, regions: MutableList<Region>) {
            val event = item.event
            val category = item.category
            val strictness = CategoryStrictness.of(category.behavior)
            var looserAllowed: Boolean? = null
            val allowed = { looserAllowed ?: isLooserAllowed(item).also { looserAllowed = it } }

            val whole = LocalWindow(event.localStart, event.localEnd)
            val seen = DayWorkWindows.partsIn(stored, whole, localNow, null, input.nightFallback)
            for ((segment, basePart) in segments(whole, seen)) {
                if (basePart?.category?.behavior == CategoryBehavior.Sleep || basePart?.category?.behavior == CategoryBehavior.PreWork) continue

                val baseStrictness = basePart?.let { CategoryStrictness.of(it.category.behavior) } ?: 0
                val inBudgetStretch = basePart?.part?.id == DayWorkWindows.BUDGET_WINDOW_PART_ID
                for (piece in byDate(segment)) {
                    regionFor(piece, category, event, strictness, baseStrictness, basePart, inBudgetStretch, allowed)?.let(regions::add)
                }
            }
        }

        private fun regionFor(
            piece: LocalWindow, category: DayCategory, event: CalendarEvent, strictness: Int, baseStrictness: Int,
            basePart: PlannedPart?, inBudgetStretch: Boolean, looserAllowed: () -> Boolean,
        ): Region? {
            val date = piece.start.toLocalDate()
            if (category.behavior == CategoryBehavior.Watch) {
                // A Work event inside a Budget day's stretch is never a part; it counts toward that day's budget, which is a
                // looser change. Without the notice it gives no credit but still takes part in overlaps.
                val holder = if (inBudgetStretch) DayWorkWindows.budgetDayHolding(stored, piece.start, input.nightFallback) else null
                if (holder != null) {
                    return Region(piece, RegionKind.BudgetCredit, category, event, strictness, budgetDate = holder, credited = looserAllowed())
                }

                if (modeOf(date) == com.accountabilibuddy.companion.model.DayMode.Budget) return null
            }

            // On a day with Comms parts of its own, mail and chat are closed outside them: a Comms piece there opens them, whatever it covers.
            if (category.behavior == CategoryBehavior.Comms && basePart?.category?.behavior != CategoryBehavior.Comms && hasStoredComms(date)) {
                return if (looserAllowed()) Region(piece, RegionKind.Part, category, event, strictness) else null
            }

            if (strictness > baseStrictness) return Region(piece, RegionKind.Part, category, event, strictness)

            if (strictness == baseStrictness) {
                return Region(piece, if (basePart?.category?.id == category.id) RegionKind.KeepBase else RegionKind.Part, category, event, strictness)
            }

            return if (looserAllowed()) Region(piece, RegionKind.Part, category, event, strictness) else null
        }

        // Overlapping events: the stricter one keeps the time they share.
        private fun stricterWins(regions: List<Region>): List<Region> {
            val placed = mutableListOf<Region>()
            val order = compareByDescending<Region> { it.strictness }
                .thenBy { it.kind }
                .thenBy { it.window.start }
                .thenBy { it.event.id }
            for (region in regions.sortedWith(order)) {
                for (free in DayTimeline.subtract(listOf(region.window), placed.map { it.window })) {
                    placed += region.copy(window = free)
                }
            }

            return placed
        }

        // The Budget credit of each Budget day: the time its credited Work events have already used, less time counted as focus.
        private fun creditByDay(placed: List<Region>): Map<LocalDate, Duration> {
            val credits = HashMap<LocalDate, Duration>()
            val untilNow = LocalWindow(EARLIEST, localNow)
            val focus = input.countedFocus.orEmpty()
            val byDay = placed.filter { it.kind == RegionKind.BudgetCredit && it.credited && it.budgetDate != null }.groupBy { it.budgetDate!! }
            for ((date, day) in byDay) {
                val passed = day.mapNotNull { DayTimeline.clip(it.window, untilNow) }
                val counted = DayTimeline.subtract(passed, focus).fold(Duration.ZERO) { sum, window -> sum.plus(Duration.between(window.start, window.end)) }
                val minutes = counted.toMinutes()
                if (minutes > 0) credits[date] = Duration.ofMinutes(minutes)
            }

            return credits
        }

        private fun hasStoredComms(date: LocalDate): Boolean = storedComms.getOrPut(date) {
            val start = date.atStartOfDay()
            DayWorkWindows.partsIn(stored, LocalWindow(start, start.plusDays(1)), localNow, null, input.nightFallback)
                .any { it.category.behavior == CategoryBehavior.Comms }
        }

        private fun placeEventPart(region: Region) {
            val date = region.window.start.toLocalDate()
            cut(date, region.window)
            cut(date.plusDays(1), region.window)

            val part = PlanPart(
                stableUuid("event|${region.event.calendarId}|${region.event.id}|${region.window.start.format(STAMP)}"),
                region.category.id,
                region.window.start.toLocalTime(),
                region.window.end.toLocalTime(),
                fromEvent = true,
            )
            partsOf(date).add(part)
            changed.add(date)
        }

        // Takes [hole] out of the parts that belong to [owner] (a part belongs to the date it ends on, so the date's evening may be
        // held by the next date's night). What is left of a part is placed again.
        private fun cut(owner: LocalDate, hole: LocalWindow) {
            val list = partsOf(owner)
            for (part in list.toList()) {
                if (part.fromEvent || isProtected(part)) continue

                val window = rawWindow(part, owner)
                if (!window.end.isAfter(hole.start) || !window.start.isBefore(hole.end)) continue

                list.removeAll { it === part }
                changed.add(owner)
                remainder(window, hole).forEachIndexed { index, piece -> place(part, piece, owner, index) }
            }
        }

        // A piece of a part goes back to the date it belongs to: a piece that ends before its owner's midnight is an evening
        // of the day before; a piece across midnight stays a part that crosses it.
        private fun place(original: PlanPart, piece: LocalWindow, owner: LocalDate, index: Int) {
            val home = if (!piece.end.isAfter(owner.atStartOfDay())) owner.minusDays(1) else owner
            if (home != owner) {
                // The next date's part took precedence over this date's evening before; it still does.
                cut(home, piece)
            }

            val placed = original.copy(
                id = if (index == 0) original.id else stableUuid("${original.id}|$index"),
                start = piece.start.toLocalTime(),
                end = piece.end.toLocalTime(),
            )
            partsOf(home).add(placed)
            changed.add(home)
        }

        private fun isProtected(part: PlanPart): Boolean {
            val behavior = stored.category(part.categoryId)?.behavior
            return behavior == CategoryBehavior.Sleep || behavior == CategoryBehavior.PreWork
        }

        // A date's template as the guards see it (a Free or Budget day without its work parts), the parts the overlay edits.
        private fun partsOf(date: LocalDate): MutableList<PlanPart> =
            parts.getOrPut(date) { DayWorkWindows.guarded(stored, stored.templateFor(date)).parts.toMutableList() }

        private fun modeOf(date: LocalDate) = modes.getOrPut(date) { stored.templateFor(date).mode }

        // A looser change counts when the phone first saw the event as it is now at least the delay before it starts, and no
        // session is running. A carried event also needs the PC's own notice, and counts from the later of the two.
        private fun isLooserAllowed(item: ClassifiedEvent): Boolean {
            if (input.sessionRunning) return false

            var knownAt = FirstKnownTimes.knownAt(known, item.event, item.category) ?: return false
            if (item.carried) {
                val since = item.carriedKnownSince ?: return false
                if (since.isAfter(input.now.plus(CLOCK_SLACK))) return false
                if (since.isAfter(knownAt)) knownAt = since
            }

            // A stamp from the future: the clock was set back after it was made, so it proves nothing.
            if (knownAt.isAfter(input.now.plus(CLOCK_SLACK))) return false

            val startsAt = item.event.localStart.atZone(input.zone).toInstant()
            return !knownAt.plus(input.looseningDelay).isAfter(startsAt)
        }
    }

    // The window cut into stretches by what the guards see under it, in order. Where parts overlap (a flexible lunch inside the
    // work), the strictest is the base, a Sleep or Pre-Work part above all; a stretch with no part has none.
    private fun segments(whole: LocalWindow, parts: List<PlannedPart>): List<Pair<LocalWindow, PlannedPart?>> {
        val edges = sortedSetOf(whole.start, whole.end)
        for (part in parts) {
            edges.add(if (part.window.start.isAfter(whole.start)) part.window.start else whole.start)
            edges.add(if (part.window.end.isBefore(whole.end)) part.window.end else whole.end)
        }

        val cuts = edges.toList()
        val result = mutableListOf<Pair<LocalWindow, PlannedPart?>>()
        var current: PlannedPart? = null
        var from = cuts[0]
        for (index in 0 until cuts.size - 1) {
            val start = cuts[index]
            val end = cuts[index + 1]
            val chosen = parts
                .filter { !it.window.start.isAfter(start) && !it.window.end.isBefore(end) }
                .sortedWith(compareByDescending<PlannedPart> { rank(it) }.thenBy { it.window.start }.thenBy { it.part.id })
                .firstOrNull()
            if (index > 0 && chosen !== current) {
                result += LocalWindow(from, start) to current
                from = start
            }

            current = chosen
        }

        result += LocalWindow(from, whole.end) to current
        return result
    }

    private fun rank(part: PlannedPart): Int =
        if (part.category.behavior == CategoryBehavior.Sleep || part.category.behavior == CategoryBehavior.PreWork) 4 else CategoryStrictness.of(part.category.behavior)

    private fun byDate(window: LocalWindow): List<LocalWindow> {
        val pieces = mutableListOf<LocalWindow>()
        var start = window.start
        while (start.isBefore(window.end)) {
            val nextMidnight = start.toLocalDate().plusDays(1).atStartOfDay()
            val end = if (nextMidnight.isBefore(window.end)) nextMidnight else window.end
            pieces += LocalWindow(start, end)
            start = end
        }

        return pieces
    }

    private fun remainder(window: LocalWindow, hole: LocalWindow): List<LocalWindow> {
        val pieces = mutableListOf<LocalWindow>()
        if (hole.start.isAfter(window.start)) pieces += LocalWindow(window.start, hole.start)
        if (hole.end.isBefore(window.end)) pieces += LocalWindow(hole.end, window.end)
        return pieces
    }

    // Where a part of [owner]'s template lies on the calendar: a part that crosses midnight starts the evening before.
    private fun rawWindow(part: PlanPart, owner: LocalDate): LocalWindow {
        val midnight = owner.atStartOfDay()
        val start = (if (part.crossesMidnight) midnight.minusDays(1) else midnight).with(part.start)
        val end = if (part.endsAtMidnight) midnight.plusDays(1) else midnight.with(part.end)
        return LocalWindow(start, end)
    }

    // The same text gives the same id on every run, so an event's part keeps its id from one minute to the next.
    private fun stableUuid(text: String): UUID {
        val hash = MessageDigest.getInstance("SHA-256").digest(text.toByteArray(Charsets.UTF_8))
        var most = 0L
        var least = 0L
        for (i in 0 until 8) most = (most shl 8) or (hash[i].toLong() and 0xFF)
        for (i in 8 until 16) least = (least shl 8) or (hash[i].toLong() and 0xFF)
        return UUID(most, least)
    }

    private val DATE: DateTimeFormatter = DateTimeFormatter.ofPattern("yyyyMMdd")
    private val STAMP: DateTimeFormatter = DateTimeFormatter.ofPattern("yyyyMMdd'T'HHmm")
}
