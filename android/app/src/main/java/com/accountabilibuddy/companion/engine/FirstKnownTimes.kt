package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.FirstKnownEntry
import java.time.Instant
import java.time.LocalDateTime

/** A calendar event with the category the guards give it (ADR 58): from this phone's rules, or one the PC resolved. */
data class ClassifiedEvent(
    val event: CalendarEvent,
    val category: DayCategory,
    /** True when the category came in a PC's sync file: it counts only under the stricter rules (fresh file, noticed by the PC and held here). */
    val carried: Boolean = false,
    /** When the PC first saw the event at this time and category (carried events only); null = not noticed. */
    val carriedKnownSince: Instant? = null,
)

/**
 * Keeps when this phone first saw each classified calendar event at its current start, end, category and what that category does
 * (ADR 58), so a looser change can be held to the notice. A change to any of them starts the notice over. An event is forgotten at the
 * end of the day after it ended or when it is no longer classified or gone from the calendar. Pure: the app passes the time. A port of
 * the PC's FirstKnownTimes.
 */
object FirstKnownTimes {
    /**
     * The entries after seeing [classified] at [now]: kept as they were when the event is unchanged, new (stamped [now]) when it is new
     * or changed, dropped once the day after the event's end is over or when it is no longer classified. Returns [existing] itself when
     * nothing changed.
     */
    fun update(existing: List<FirstKnownEntry>, classified: Iterable<ClassifiedEvent>, now: Instant, localNow: LocalDateTime): List<FirstKnownEntry> {
        val known = byEvent(existing)
        val result = mutableListOf<FirstKnownEntry>()
        val seen = HashSet<Pair<String, String>>()
        var changed = false
        for ((event, category) in classified) {
            val key = event.calendarId to event.id
            if (!event.localEnd.toLocalDate().plusDays(2).atStartOfDay().isAfter(localNow) || !seen.add(key)) continue

            val entry = known[key]
            if (entry != null && matches(entry, event, category)) {
                result += entry
            } else {
                result += FirstKnownEntry(event.calendarId, event.id, event.localStart, event.localEnd, category.id, category.behavior, now)
                changed = true
            }
        }

        changed = changed || result.size != existing.size
        return if (changed) result else existing
    }

    /**
     * [updated] with back the entries of [existing] that [keep] selects and [updated] dropped, for events that are not known to be gone
     * (the phone's own calendar before its first read has landed). Returns [updated] itself when nothing is added back.
     */
    fun keeping(existing: List<FirstKnownEntry>, updated: List<FirstKnownEntry>, keep: (FirstKnownEntry) -> Boolean): List<FirstKnownEntry> {
        val have = byEvent(updated)
        val missing = existing.filter { keep(it) && (it.calendarId to it.eventId) !in have }
        return if (missing.isEmpty()) updated else updated + missing
    }

    /** When the event was first seen at its current start, end, [category] and behaviour; null when it wasn't (or has changed since, so the notice starts over). */
    fun knownAt(known: Map<Pair<String, String>, FirstKnownEntry>, event: CalendarEvent, category: DayCategory): Instant? {
        val entry = known[event.calendarId to event.id] ?: return null
        return if (matches(entry, event, category)) entry.knownAt else null
    }

    /** The entries by (calendar id, event id). */
    fun byEvent(entries: Iterable<FirstKnownEntry>): Map<Pair<String, String>, FirstKnownEntry> =
        entries.associateBy { it.calendarId to it.eventId }

    private fun matches(entry: FirstKnownEntry, event: CalendarEvent, category: DayCategory): Boolean =
        entry.localStart == event.localStart && entry.localEnd == event.localEnd && entry.categoryId == category.id && entry.behavior == category.behavior
}
