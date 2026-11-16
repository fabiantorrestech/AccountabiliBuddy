package com.accountabilibuddy.shared.session

/**
 * What ending the running session early costs (the mode switch's "now" costs the same, ADR 68; plan check S4/S8). Pure twin of the app's `SessionEndCost`.
 *
 * @property free No wait and no reason: the session ends on command, or ProductaBuddy, or a break.
 * @property hasSetEnd False for a session with no set end: "after this session ends" then means when it is ended by hand.
 */
data class EndCostTerms(val waitSeconds: Int, val minReasonLength: Int, val free: Boolean, val hasSetEnd: Boolean)

object SessionEndCostRules {
    /**
     * The phone's own session (PS6a review S4): never free in AccountabiliBuddy while it has a set end. The cost is what the phone's End asks
     * ([PhoneSessionEndRules.terms]: the phone's own wait and reason setting); ProductaBuddy and a session with no set end end at once, so they are free.
     */
    fun own(mode: PhoneSessionMode, record: PhoneSessionRecord, settings: PhoneEndSettings): EndCostTerms {
        val terms = PhoneSessionEndRules.terms(mode, record, settings)
        return EndCostTerms(terms.wait.seconds.toInt(), terms.minReasonLength, free = terms.isNone, hasSetEnd = !record.noSetEnd)
    }

    /**
     * Two sessions run (an offline double start): the stricter cost. It is free only when both are; the wait and the reason are the longest of the
     * ones that cost something; with no set end on either, "after this session ends" means by hand.
     */
    fun stricter(a: EndCostTerms?, b: EndCostTerms?): EndCostTerms? {
        if (a == null || b == null) return a ?: b
        val paying = listOf(a, b).filter { !it.free }
        return EndCostTerms(
            waitSeconds = paying.maxOfOrNull { it.waitSeconds } ?: 0,
            minReasonLength = paying.maxOfOrNull { it.minReasonLength } ?: 0,
            free = paying.isEmpty(),
            hasSetEnd = a.hasSetEnd && b.hasSetEnd,
        )
    }
}
