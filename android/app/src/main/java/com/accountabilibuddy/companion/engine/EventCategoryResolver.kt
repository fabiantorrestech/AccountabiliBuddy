package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.CalendarRuleMode
import com.accountabilibuddy.companion.model.CalendarRuleSet
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.ClassificationScope
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.EventClassification
import java.time.Duration
import java.util.UUID

/**
 * Which day plan category a calendar event carries (ADR 58), a port of the PC's EventCategoryResolver. The PC's priority is this
 * event, then its series, then keywords, then the every-event rule, for a phone calendar's event (the choices are this phone's own, ADR 64); one the PC sent already
 * carries the category the PC resolved ([applicable] checks it against this phone's plan). All-day events, zero-length events and events longer
 * than [LONGEST_EVENT] carry nothing, and neither does a category that is gone or is Sleep or Pre-Work, even when the PC sent it.
 */
object EventCategoryResolver {
    /** A longer event is ignored (it can only make the day stricter than it would be). */
    val LONGEST_EVENT: Duration = Duration.ofDays(7)

    /** True for a timed event that has a length and isn't longer than [LONGEST_EVENT]. */
    fun isUsable(event: CalendarEvent): Boolean {
        val length = Duration.between(event.localStart, event.localEnd)
        return !event.isAllDay && !length.isZero && !length.isNegative && length <= LONGEST_EVENT
    }

    /** The category a phone calendar's event carries, by a choice on it or its series, else the calendar's rule; null when it carries none. */
    fun categoryOf(event: CalendarEvent, plan: DayPlan, rules: CalendarRuleSet, classifications: List<EventClassification> = emptyList()): DayCategory? {
        if (!isUsable(event)) return null
        chosen(event, classifications)?.let { return applicable(plan, it.categoryId) }
        if (rules.isEmpty) return null
        return applicable(plan, fromRule(event, rules))
    }

    /** The choice that applies to [event]: on it first, then on its series; null for none. */
    fun chosen(event: CalendarEvent, classifications: List<EventClassification>): EventClassification? =
        classifications.firstOrNull { it.calendarId == event.calendarId && it.scope == ClassificationScope.ThisEvent && it.key == event.classificationKey }
            ?: event.seriesId?.let { series -> classifications.firstOrNull { it.calendarId == event.calendarId && it.scope == ClassificationScope.WholeSeries && it.key == series } }

    /**
     * The event's category was chosen by hand rather than by a rule; "Don't classify" isn't a category. Only such an event may lift Pre-Work for its time (ADR 64): a rule
     * made once shouldn't quietly open a guard on every later day.
     */
    fun isChosenByHand(event: CalendarEvent, classifications: List<EventClassification>): Boolean = chosen(event, classifications)?.categoryId != null

    /** [categoryId] as a category of [plan], or null when it is gone, unknown, or Sleep or Pre-Work (a night or a warm-up has its own logic that a meeting doesn't fit). */
    fun applicable(plan: DayPlan, categoryId: UUID?): DayCategory? {
        val category = categoryId?.let { plan.category(it) } ?: return null
        return category.takeUnless { it.behavior == CategoryBehavior.Sleep || it.behavior == CategoryBehavior.PreWork }
    }

    private fun fromRule(event: CalendarEvent, rules: CalendarRuleSet): UUID? {
        val rule = rules.ruleFor(event.calendarId) ?: return null
        return when (rule.mode) {
            CalendarRuleMode.EveryEvent -> rule.categoryId
            CalendarRuleMode.Keywords -> rules.keywordsFor(rule)
                .firstOrNull { it.keyword.isNotEmpty() && event.title.contains(it.keyword, ignoreCase = true) }
                ?.categoryId
            CalendarRuleMode.Off -> null
        }
    }
}
