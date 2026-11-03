package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.CalendarRuleMode
import com.accountabilibuddy.companion.model.CalendarRuleSet
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.DayPlan
import java.time.Duration
import java.util.UUID

/**
 * Which day plan category a calendar event carries (ADR 58), a port of the PC's EventCategoryResolver. The PC's priority is this
 * event, then its series, then keywords, then the every-event rule; the phone has no per-event choice yet (tap-to-classify follows
 * later), so here a phone calendar's event gets its category from the calendar's rule alone, and one the PC sent already carries the
 * category the PC resolved ([applicable] checks it against this phone's plan). All-day events, zero-length events and events longer
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

    /** The category a phone calendar's rule gives [event], or null when it carries none. */
    fun categoryOf(event: CalendarEvent, plan: DayPlan, rules: CalendarRuleSet): DayCategory? {
        if (rules.isEmpty || !isUsable(event)) return null
        return applicable(plan, fromRule(event, rules))
    }

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
