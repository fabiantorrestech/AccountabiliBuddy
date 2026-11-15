package com.accountabilibuddy.shared.session

import java.time.Duration
import java.time.Instant

/** The friction before ending early (the phone's own setting, defaults as the PC's `BypassSettings.Default`: 60 seconds, 15 characters). */
data class PhoneEndSettings(val waitSeconds: Int = 60, val minReasonLength: Int = 15) {
    companion object {
        val DEFAULT = PhoneEndSettings()
    }
}

/** What ending a session early takes: a wait counted from opening End and a reason of at least [minReasonLength] characters. */
data class PhoneEndTerms(val wait: Duration, val minReasonLength: Int) {
    val isNone: Boolean get() = wait.isZero && minReasonLength == 0

    companion object {
        val NONE = PhoneEndTerms(Duration.ZERO, 0)
    }
}

/** R8 and R9 as pure rules. */
object PhoneSessionEndRules {
    /** The reason "All tasks done" ends with (R9). */
    const val ALL_TASKS_DONE_REASON = "All tasks done"

    /** A session with no set end, or a test, ends on command: nothing there ends "early" (the PC's `BypassPolicy.EndsOnCommand`). */
    fun endsOnCommand(record: PhoneSessionRecord): Boolean = record.isTest || record.noSetEnd

    /**
     * What ending [record] early takes in [mode]: AccountabiliBuddy asks the wait and the reason of a timed session; a session that ends
     * on command, and every ProductaBuddy session, takes none. The mode is read live (S3), so a PB to AB change applies from then.
     */
    fun terms(mode: PhoneSessionMode, record: PhoneSessionRecord, settings: PhoneEndSettings): PhoneEndTerms = when {
        mode == PhoneSessionMode.PRODUCTA_BUDDY -> PhoneEndTerms.NONE
        endsOnCommand(record) -> PhoneEndTerms.NONE
        else -> PhoneEndTerms(Duration.ofSeconds(settings.waitSeconds.toLong()), settings.minReasonLength)
    }

    /** The reason is long enough: its trimmed length counts (the PC's `BypassPolicy.Check`). */
    fun reasonLongEnough(terms: PhoneEndTerms, reason: String?): Boolean = (reason?.trim()?.length ?: 0) >= terms.minReasonLength

    /** When an End opened at [requestedAt] may be confirmed for good. */
    fun readyAt(terms: PhoneEndTerms, requestedAt: Instant): Instant = requestedAt.plus(terms.wait)

    /** R9: "All tasks done" ends it at once (a running session whose checklist holds [PhoneSessionChecklist.canEndAtOnce]). */
    fun canEndAtOnce(record: PhoneSessionRecord, now: Instant): Boolean = record.isRunning && record.checklist.canEndAtOnce(now)
}
