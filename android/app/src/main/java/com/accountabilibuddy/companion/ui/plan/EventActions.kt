package com.accountabilibuddy.companion.ui.plan

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.calendar.PhoneCalendarReader
import com.accountabilibuddy.companion.engine.CategoryStrictness
import com.accountabilibuddy.companion.engine.EventCategoryResolver
import com.accountabilibuddy.companion.engine.EventKeywordRules
import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.ClassificationScope
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.EventClassification
import com.accountabilibuddy.companion.placement.PlacementSession
import com.accountabilibuddy.companion.sync.PcFileRemote
import java.time.Instant
import java.util.UUID

/**
 * What the event sheet does (ADR 64), and where it goes. An event in a PC's sync file belongs to that PC: the phone asks, and the PC decides under its own rules (a
 * session refuses it, a looser change waits) and shows the result in its next file. An event only in this phone's own calendar is decided here, by the same rules.
 * Every method answers with one line for the user.
 */
class EventActions(private val app: CompanionApp) {
    /** True for an event only in this phone's own calendar. */
    fun isOwn(event: CalendarEvent): Boolean = event.calendarId.startsWith(PhoneCalendarReader.ID_PREFIX)

    /** The categories an event can be given: Sleep and Pre-Work never (ADR 58). */
    fun categories(): List<DayCategory> =
        app.store.state.value.document.dayPlan.categories.filter { it.behavior != CategoryBehavior.Sleep && it.behavior != CategoryBehavior.PreWork }

    /** The category the event counts as now; null when the plan decides. */
    fun currentCategory(event: CalendarEvent): DayCategory? {
        val plan = app.store.state.value.document.dayPlan
        val fromFile = event.categoryId?.let { EventCategoryResolver.applicable(plan, it) }
        if (!isOwn(event)) return fromFile
        return EventCategoryResolver.categoryOf(event, plan, app.calendarRules.rules.value.active, app.eventClassifications.classifications.value)
    }

    /** True when the choice has more than one reach: an own event that repeats, or a PC's event, whose series only the PC knows. */
    fun mayRepeat(event: CalendarEvent): Boolean = !isOwn(event) || event.seriesId != null

    /** True while a keyword rule can be made: not on a calendar of this phone that gives every event one category. */
    fun canMakeRule(event: CalendarEvent): Boolean =
        EventCategoryResolver.isUsable(event) && (!isOwn(event) || EventKeywordRules.withKeyword(app.calendarRules.rules.value.active, event.calendarId, "x", UUID.randomUUID()) != null)

    /** Chooses [category], or "Don't classify" with null. */
    fun classify(event: CalendarEvent, category: DayCategory?, wholeSeries: Boolean): String {
        if (!isOwn(event)) {
            val choice = if (category != null) PcFileRemote.EVENT_CHOICE_CATEGORY else PcFileRemote.EVENT_CHOICE_DONT
            return sentToPc(app.pcRemote.classifyEvent(event, if (wholeSeries) PcFileRemote.EVENT_SCOPE_SERIES else PcFileRemote.EVENT_SCOPE_THIS, choice, category?.id))
        }

        val current = currentCategory(event)
        val looser = current != null && (category == null || CategoryStrictness.of(category.behavior) < CategoryStrictness.of(current.behavior))
        return saveOwn(target(event, wholeSeries, category?.id), removes = false, looser = looser)
    }

    /** Takes the choice back so the rules or the plan decide again. */
    fun backToAutomatic(event: CalendarEvent, wholeSeries: Boolean): String {
        if (!isOwn(event)) {
            return sentToPc(app.pcRemote.classifyEvent(event, if (wholeSeries) PcFileRemote.EVENT_SCOPE_SERIES else PcFileRemote.EVENT_SCOPE_THIS, PcFileRemote.EVENT_CHOICE_AUTOMATIC, null))
        }

        return saveOwn(target(event, wholeSeries, null), removes = true, looser = currentCategory(event) != null)
    }

    /** "Always classify like this": a keyword rule, which waits like any rule change. */
    fun makeRule(event: CalendarEvent, keyword: String, category: DayCategory): String {
        if (keyword.isBlank()) return "Type the words to look for first."
        if (!isOwn(event)) {
            return sentToPc(app.pcRemote.classifyEvent(event, PcFileRemote.EVENT_SCOPE_KEYWORD, PcFileRemote.EVENT_CHOICE_CATEGORY, category.id, keyword.trim()))
        }

        val now = Instant.now()
        val rules = app.calendarRules.rules.value
        val changed = EventKeywordRules.withKeyword(rules.pending ?: rules.active, event.calendarId, keyword, category.id)
            ?: return "This calendar already gives every event one category."
        return if (app.calendarRules.trySave(changed, now, sessionRunning(now))) {
            "Saved. It applies after the usual wait, and never during a session."
        } else {
            LOCKED
        }
    }

    /** A Later item titled after the event, due that day; it never touches today's three tasks. */
    fun addTask(event: CalendarEvent): String =
        if (app.sharedLists.addLater(event.title, null, null, event.localStart.toLocalDate().toString()) != null) "Added to Later, due that day." else "It couldn't be added."

    private fun target(event: CalendarEvent, wholeSeries: Boolean, categoryId: UUID?): EventClassification {
        val series = event.seriesId.takeIf { wholeSeries }
        return if (series != null) {
            EventClassification(event.calendarId, series, ClassificationScope.WholeSeries, categoryId)
        } else {
            EventClassification(event.calendarId, event.classificationKey, ClassificationScope.ThisEvent, categoryId)
        }
    }

    private fun saveOwn(target: EventClassification, removes: Boolean, looser: Boolean): String {
        val now = Instant.now()
        return when {
            !app.eventClassifications.trySet(target, removes, looser, now, sessionRunning(now)) -> LOCKED
            looser -> "Saved. It's less strict, so it takes over after the usual wait."
            else -> "Saved. It applies if it was known ahead of the event."
        }
    }

    private fun sentToPc(problem: String?): String = problem ?: "Sent to ${app.pcRemote.target()?.file?.device?.name ?: "the PC"}. It shows here after its next sync."

    private fun sessionRunning(now: Instant): Boolean = PlacementSession.current(app, now, ignoreOff = true) != null

    private companion object {
        const val LOCKED = "Locked during a focus session."
    }
}
