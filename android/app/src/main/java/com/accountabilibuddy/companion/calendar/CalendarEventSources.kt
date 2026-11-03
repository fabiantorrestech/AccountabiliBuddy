package com.accountabilibuddy.companion.calendar

import com.accountabilibuddy.companion.engine.CategoryStrictness
import com.accountabilibuddy.companion.engine.ClassifiedEvent
import com.accountabilibuddy.companion.engine.EventCategoryResolver
import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.CalendarRuleSet
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.DayPlan
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.util.Locale

/** One PC's sync file as the overlay sees it: when the PC wrote it and its calendar events (the ones that carry a category are the ones that count). */
data class PcCalendarFile(val deviceId: String, val writtenAt: Instant, val events: List<CalendarEvent>)

/**
 * Brings the two sources of classified events together (ADR 58): this phone's own calendar (the phone's rules give the category) and the
 * PC's sync files (the PC gave the category). The rules, written once, fail strict:
 * - A PC file counts only while it is under [FILE_FRESH_FOR] old (by the phone's clock) and not from the future; a stale file is ignored
 *   whole, stricter events included, so the phone follows the plan. A carried event's category must exist in this phone's plan and not be
 *   Sleep or Pre-Work, else the plan stands. A carried event still has to be held here for the delay and carry the PC's own
 *   `knownSince` to loosen anything (the engine checks, see [ClassifiedEvent.carried]).
 * - The same event from both (matched by title, start and end: Android's calendar ids and the file's ids are not known to be the same, and
 *   the calendar ids never are): this phone's copy supplies the times, since it is the fresher. If both gave a category the stricter one
 *   is used and stays "carried" when it is the PC's, so it keeps the stricter proof; if only one gave a category that one is used.
 */
object CalendarEventSources {
    /** A PC file older than this is not trusted at all (autosync has lagged by hours; proposed in the plan, reasoning only). */
    val FILE_FRESH_FOR: Duration = Duration.ofHours(24)

    private val CLOCK_SLACK: Duration = Duration.ofMinutes(1)

    fun merge(
        plan: DayPlan,
        rules: CalendarRuleSet,
        owned: List<CalendarEvent>,
        files: List<PcCalendarFile>,
        now: Instant,
    ): List<ClassifiedEvent> {
        val carried = carriedFrom(plan, files, now)
        val carriedByLook = HashMap<Triple<String, LocalDateTime, LocalDateTime>, MutableList<ClassifiedEvent>>()
        carried.forEach { carriedByLook.getOrPut(lookOf(it.event)) { mutableListOf() }.add(it) }

        val result = ArrayList<ClassifiedEvent>()
        val matched = HashSet<ClassifiedEvent>()
        for (event in owned) {
            val own = EventCategoryResolver.categoryOf(event, plan, rules)
            val twins = carriedByLook[lookOf(event)].orEmpty()
            val twin = twins.maxByOrNull { strictness(it.category) }
            matched.addAll(twins)
            val item = when {
                twin == null -> own?.let { ClassifiedEvent(event, it) }
                own == null || strictness(twin.category) > strictness(own) ->
                    ClassifiedEvent(event, twin.category, carried = true, carriedKnownSince = twin.carriedKnownSince)
                else -> ClassifiedEvent(event, own)
            }
            if (item != null) result += item
        }

        carried.filter { it !in matched }.forEach { result += it }
        return result
    }

    /** The events of the fresh PC files that carry a usable category of this phone's plan, as carried events. */
    fun carriedFrom(plan: DayPlan, files: List<PcCalendarFile>, now: Instant): List<ClassifiedEvent> {
        val result = ArrayList<ClassifiedEvent>()
        for (file in files) {
            if (file.writtenAt.isAfter(now.plus(CLOCK_SLACK)) || Duration.between(file.writtenAt, now) > FILE_FRESH_FOR) continue

            for (event in file.events) {
                val category = event.categoryId?.let { EventCategoryResolver.applicable(plan, it) } ?: continue
                if (!EventCategoryResolver.isUsable(event)) continue

                // A PC's ids stay apart from this phone's and from another PC's.
                val keyed = event.copy(calendarId = "pc:${file.deviceId}:${event.calendarId}")
                result += ClassifiedEvent(keyed, category, carried = true, carriedKnownSince = event.knownSince)
            }
        }

        return result
    }

    private fun lookOf(event: CalendarEvent) = Triple(event.title.trim().lowercase(Locale.ROOT), event.localStart, event.localEnd)

    private fun strictness(category: DayCategory): Int = CategoryStrictness.of(category.behavior)
}
