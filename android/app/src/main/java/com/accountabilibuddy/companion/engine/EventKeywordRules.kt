package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.CalendarRule
import com.accountabilibuddy.companion.model.CalendarRuleMode
import com.accountabilibuddy.companion.model.CalendarRuleSet
import com.accountabilibuddy.companion.model.KeywordCategory
import java.util.UUID

/** "Always classify like this" on a calendar event (ADR 64), the same as the PC's EventKeywordRules. */
object EventKeywordRules {
    private val TRAILING = Regex("""[\s\-–—:#(]*\d[\d\s/.\-:)]*$""")

    /** The event's title without what changes between meetings at its end: "Team standup - 11/04" gives "Team standup". */
    fun suggestKeyword(title: String): String {
        val trimmed = TRAILING.replace(title.trim(), "").trim()
        return trimmed.ifEmpty { title.trim() }
    }

    /**
     * [set] with the keyword added for one calendar. A calendar with no rule, or Off, gets a keyword rule with its own list holding only this
     * keyword; a keyword rule keeps its keywords and puts this one first, as a list of its own. Null on a calendar whose rule gives every event one category.
     */
    fun withKeyword(set: CalendarRuleSet, calendarId: String, keyword: String, categoryId: UUID): CalendarRuleSet? {
        val rule = set.ruleFor(calendarId)
        if (rule?.mode == CalendarRuleMode.EveryEvent) return null
        val added = KeywordCategory(keyword.trim(), categoryId)
        val kept = if (rule?.mode == CalendarRuleMode.Keywords) set.keywordsFor(rule).filterNot { it.keyword.equals(added.keyword, ignoreCase = true) } else emptyList()
        val changed = CalendarRule(calendarId, CalendarRuleMode.Keywords, null, listOf(added) + kept)
        return set.copy(rules = set.rules.filterNot { it.calendarId == calendarId } + changed)
    }
}
