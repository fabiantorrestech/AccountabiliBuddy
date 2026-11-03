package com.accountabilibuddy.companion.calendar

import com.accountabilibuddy.companion.model.CalendarEvent

/**
 * What this phone last saw of its own calendar (ADR 58), and whether it has seen it at all since the app started. Pure, so the rules
 * for a failed read can be tried without Android:
 * - A read that succeeded replaces the view and marks it [loaded].
 * - A read that failed while the permission is still granted keeps the last view (the strict way: neither wiping first-seen stamps nor
 *   lifting stricter events because of a hiccup in the calendar provider).
 * - A read that could not be made because the permission is gone clears the view and counts as loaded: the events are really gone.
 * Until the first read has landed, [loaded] is false, and the caller must not forget first-seen stamps of this phone's own events.
 */
class OwnEventsView {
    @Volatile
    var events: List<CalendarEvent> = emptyList()
        private set

    @Volatile
    var loaded: Boolean = false
        private set

    /** Bumped whenever [events] changes. */
    @Volatile
    var version: Int = 0
        private set

    /** Applies a read ([read] null = it failed or was not allowed). True when [events] changed. */
    @Synchronized
    fun applyRead(read: List<CalendarEvent>?, permitted: Boolean): Boolean {
        if (read == null) return if (permitted) false else clear()

        loaded = true
        if (read == events) return false

        events = read
        version++
        return true
    }

    /** Nothing is to be read (the switch is off, or every calendar is Off): the view is empty. True when it changed. */
    @Synchronized
    fun clear(): Boolean {
        loaded = true
        if (events.isEmpty()) return false

        events = emptyList()
        version++
        return true
    }
}
