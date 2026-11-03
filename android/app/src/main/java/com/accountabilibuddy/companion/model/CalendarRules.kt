package com.accountabilibuddy.companion.model

import java.time.Instant
import java.time.LocalDateTime
import java.util.UUID

/** What a calendar's rule does (ADR 58; the PC's CalendarRuleMode). */
enum class CalendarRuleMode {
    /** The calendar's events get no category from the calendar (the default). */
    Off,

    /** An event whose title contains a keyword gets that keyword's category. */
    Keywords,

    /** Every event of the calendar gets one category. */
    EveryEvent,
}

/** A keyword and the category an event with it in its title gets (the PC's KeywordCategory). */
data class KeywordCategory(val keyword: String, val categoryId: UUID)

/** One calendar's rule: [categoryId] for [CalendarRuleMode.EveryEvent]; [ownKeywords] null = the global list (the PC's CalendarRule). */
data class CalendarRule(
    val calendarId: String,
    val mode: CalendarRuleMode,
    val categoryId: UUID? = null,
    val ownKeywords: List<KeywordCategory>? = null,
)

/**
 * The phone's own calendar rules and the global keyword list they share (ADR 58; the PC's CalendarRuleSet). A calendar with no rule is
 * Off. [enabled] is the phone's switch for reading its own calendar at all: off, the rules do nothing and the permission is not asked for.
 */
data class CalendarRuleSet(
    val enabled: Boolean = false,
    val rules: List<CalendarRule> = emptyList(),
    val globalKeywords: List<KeywordCategory> = emptyList(),
) {
    /** True when no calendar can give an event a category (switch off, or every rule Off). */
    val isEmpty: Boolean get() = !enabled || rules.all { it.mode == CalendarRuleMode.Off }

    fun ruleFor(calendarId: String): CalendarRule? = rules.firstOrNull { it.calendarId == calendarId }

    /** The keywords a calendar's keyword rule uses: its own list, else the global one. */
    fun keywordsFor(rule: CalendarRule): List<KeywordCategory> = rule.ownKeywords ?: globalKeywords

    companion object {
        val Empty = CalendarRuleSet()
    }
}

/**
 * The rules in force plus a change waiting to take over (ADR 58; the PC's CalendarRules). Changing a rule is a looser change: the new set
 * waits for the looser-change delay and never takes over during a session. [pendingTakesOverAt] is by the phone's clock.
 */
data class CalendarRules(
    val active: CalendarRuleSet = CalendarRuleSet.Empty,
    val pending: CalendarRuleSet? = null,
    val pendingTakesOverAt: Instant? = null,
) {
    /** True when a waiting set's time has come. */
    fun pendingIsDue(now: Instant): Boolean = pending != null && pendingTakesOverAt != null && !pendingTakesOverAt.isAfter(now)

    /** The waiting set made the active one (this when none waits). */
    fun promoted(): CalendarRules = if (pending == null) this else CalendarRules(pending)

    /** A new set to take over at [takesOverAt], replacing any set already waiting (the wait starts over). */
    fun withPending(set: CalendarRuleSet, takesOverAt: Instant): CalendarRules = copy(pending = set, pendingTakesOverAt = takesOverAt)
}

/**
 * When this phone first saw a classified calendar event at its current start, end and category (ADR 58). Changing any of the three, or
 * what the category does ([behavior]), starts the notice over. Kept on this phone only.
 */
data class FirstKnownEntry(
    val calendarId: String,
    val eventId: String,
    val localStart: LocalDateTime,
    val localEnd: LocalDateTime,
    val categoryId: UUID,
    val behavior: CategoryBehavior,
    val knownAt: Instant,
)
