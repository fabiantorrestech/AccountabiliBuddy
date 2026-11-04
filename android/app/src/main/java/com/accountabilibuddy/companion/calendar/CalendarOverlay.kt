package com.accountabilibuddy.companion.calendar

import com.accountabilibuddy.companion.engine.CalendarDayOverlay
import com.accountabilibuddy.companion.engine.CalendarOverlayInput
import com.accountabilibuddy.companion.engine.ClassifiedEvent
import com.accountabilibuddy.companion.engine.FirstKnownTimes
import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.CalendarRules
import com.accountabilibuddy.companion.model.DayMode
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.SleepTimes
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

/**
 * The day plan the guards read (ADR 58): the stored plan with classified calendar events laid over it, in memory only
 * ([CalendarDayOverlay]). Plan editors, sync and the plan hash keep using the stored plan. With the switch off (and no change waiting to
 * switch it on) [effective] returns the stored plan itself after one volatile read: no permission is asked, nothing is read and nothing
 * runs off the caller's thread, so behaviour is exactly as before. A session counts as running when [sessionRunning] says so (breaks too),
 * and then nothing loosens or promotes.
 *
 * The result is cached per stored plan and per minute until the events, rules or session state change. First-known stamps are kept
 * up to date whenever a result is worked out and written off the calling thread. The phone's own events are read in the background by
 * [refreshOwn] (the caller's thread must not be the main one), never inside [effective].
 *
 * Clock: every wait on the phone uses its wall clock ([Instant.now] via [clock]); there is no trusted clock here. The hold compares the
 * stamp with the event's start, so setting the clock forward does not help. The weakness is setting it back 10 minutes or more, letting
 * a new event be stamped, and setting it right again: the stamp is then old enough. A stamp that ends up in the future is ignored, but
 * setting the clock back even 2 minutes makes [CalendarEventSources] drop fresh PC files (their `writtenAt` reads as the future), which
 * lifts stricter carried events until the clock is right. Same weakness as the phone's other waits (PlanWait, SyncOffWait, CalendarRulesStore).
 */
class CalendarOverlay(
    private val rulesStore: CalendarRulesStore,
    private val classifications: EventClassificationStore,
    private val firstKnown: FirstKnownStore,
    private val reader: PhoneCalendarReader,
    private val sessionRunning: (Instant) -> Boolean,
    private val pcFiles: () -> List<PcCalendarFile>,
    private val countedFocus: (Instant, ZoneId) -> List<LocalWindow>,
    private val onOwnEventsChanged: () -> Unit = {},
    /** Called before each read of the phone's calendar while the switch is on, so the app can start watching it for changes (idempotent). */
    private val watch: () -> Unit = {},
    private val clock: () -> Instant = Instant::now,
    private val zone: () -> ZoneId = ZoneId::systemDefault,
) {
    private class Cached(val stored: DayPlan, val key: List<Any?>, val plan: DayPlan)

    private val gate = Any()
    private val saver = Executors.newSingleThreadExecutor { runnable -> Thread(runnable, "calendar-overlay-save").apply { isDaemon = true } }
    private val refreshing = AtomicBoolean(false)

    private val own = OwnEventsView()

    @Volatile
    private var ownedAt: Instant = Instant.MIN

    private var cached: Cached? = null

    /** The events read from this phone's own calendar, for the Plan timeline; empty while the switch is off. */
    val ownEvents: List<com.accountabilibuddy.companion.model.CalendarEvent> get() = own.events

    /** The rules in force and any change waiting (for the rule page). */
    val rules get() = rulesStore.rules

    /** True when the switch is on, or a change that would switch it on is waiting. */
    private fun active(): Boolean {
        val current = rulesStore.rules.value
        return current.active.enabled || current.pending != null
    }

    /** The plan the guards and displays read; [stored] itself when the feature is off or nothing applies. */
    fun effective(stored: DayPlan, nightFallback: ((LocalDate) -> SleepTimes?)?): DayPlan {
        if (!active()) return stored

        val now = clock()
        val running = sessionRunning(now)
        val current = rulesStore.settle(now, running)
        if (!current.active.enabled) return stored
        val chosen = classifications.settle(now, running)

        refreshOwnSoon(now)
        val files = pcFiles()
        val minute = now.epochSecond / 60
        val key = listOf(own.version, own.loaded, current.active, chosen, files.map { it.writtenAt to it.events.size }, running, minute, nightFallback != null)
        synchronized(gate) {
            cached?.let { if (it.stored === stored && it.key == key) return it.plan }

            val local = LocalDateTime.ofInstant(now, zone())
            val loaded = own.loaded
            val classified: List<ClassifiedEvent> = CalendarEventSources.merge(stored, current.active, own.events, files, now, chosen)
            val updated = FirstKnownTimes.update(firstKnown.entries, classified, now, local)
            // Until the first read of the phone's calendar has landed (a fresh start), its events are not known to be gone: keep their stamps.
            val next = if (loaded) updated else FirstKnownTimes.keeping(firstKnown.entries, updated) { it.calendarId.startsWith(PhoneCalendarReader.ID_PREFIX) }
            if (firstKnown.replace(next)) saver.execute { firstKnown.save() }
            val hasBudgetDay = stored.templates.any { it.template.mode == DayMode.Budget } || stored.oneOffDays.any { it.template.mode == DayMode.Budget }
            val plan = CalendarDayOverlay.apply(
                stored,
                CalendarOverlayInput(
                    classified, firstKnown.entries, now, zone(), running, LOOSENING_DELAY, nightFallback,
                    if (hasBudgetDay) countedFocus(now, zone()) else null,
                ),
            )
            cached = Cached(stored, key, plan)
            return plan
        }
    }

    /** Reads the phone's own calendar now if the switch is on and the permission is granted; runs on the caller's thread (not the main one). */
    fun refreshOwn() {
        refreshNow(clock())
    }

    /** True when the app may read the phone's calendar (the permission is granted). */
    fun hasPermission(): Boolean = reader.hasPermission()

    /** The phone's calendars for the rule page; empty without the permission. Call off the main thread. */
    fun calendars(): List<PhoneCalendar> = reader.calendars()

    /** True when the phone has been asked to read its own calendar (the switch is on). */
    fun wantsOwnCalendar(): Boolean = rulesStore.rules.value.active.enabled

    private fun refreshOwnSoon(now: Instant) {
        // A read stamped in the future (the clock was set back) is stale, not fresh: it must not freeze the phone's view of its calendar.
        val age = Duration.between(ownedAt, now)
        if ((!age.isNegative && age < OWN_FRESH_FOR) || !refreshing.compareAndSet(false, true)) return

        saver.execute {
            try {
                refreshNow(clock())
            } catch (failure: Exception) {
                // A calendar provider that misbehaves is a failed read: the last view stays, and the process does not die.
            } finally {
                refreshing.set(false)
            }
        }
    }

    private fun refreshNow(now: Instant) {
        val rules = rulesStore.rules.value.active
        ownedAt = now
        if (!rules.enabled || rules.rules.all { it.mode == com.accountabilibuddy.companion.model.CalendarRuleMode.Off }) {
            if (own.clear()) onOwnEventsChanged()
            return
        }

        watch()
        val ids = rules.rules.filter { it.mode != com.accountabilibuddy.companion.model.CalendarRuleMode.Off }.map { it.calendarId }.toSet()
        val read = reader.events(ids, now.minus(Duration.ofDays(8)), now.plus(Duration.ofDays(15)), zone())
        // A failed read while the permission is still granted keeps the last view; a permission that is gone empties it (see OwnEventsView).
        if (own.applyRead(read, reader.hasPermission())) onOwnEventsChanged()
    }

    companion object {
        /** The phone's own looser-change wait (CalendarRulesStore.DELAY, as its other waits; the PC's setting does not reach the phone). */
        val LOOSENING_DELAY: Duration = CalendarRulesStore.DELAY
        private val OWN_FRESH_FOR: Duration = Duration.ofMinutes(2)
    }
}
