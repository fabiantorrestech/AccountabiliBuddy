package com.accountabilibuddy.shared.session

import java.time.Instant

/**
 * What the controller does after a transition (the engine only says): the engine never touches a file, an alarm or an app.
 * Every effect that carries a time carries a trusted time.
 */
sealed interface PhoneSessionEffect {
    /** Save the checkpoint (the sealed store). */
    data object Persist : PhoneSessionEffect

    /** Write the sync file now (never for a test session). */
    data object WriteSyncFile : PhoneSessionEffect

    /** The exact alarm for the set end; null cancels it. */
    data class ScheduleEndAlarm(val at: Instant?) : PhoneSessionEffect

    /** The exact alarm for an armed end (R8); null cancels it. */
    data class ScheduleArmedAlarm(val at: Instant?) : PhoneSessionEffect

    /** Ask the enforcement (LockMyPhone, Curbox) to hold until [endsAt] (null: no set end): at the start, on resume and after +10 (R10). */
    data class EnforceUntil(val endsAt: Instant?) : PhoneSessionEffect

    /** The session ended: release what this session asked for (only that, ADR 85). */
    data class ReleaseEnforcement(val kind: PhoneSessionEndKind) : PhoneSessionEffect

    /** Keep an end reason (Keystore-sealed 90 days; never synced, never logged). */
    data class SaveReason(val reason: String) : PhoneSessionEffect {
        override fun toString(): String = "SaveReason"
    }

    /** Tell the user what the phone being off did (times only; R13). [closed] = the session ended at [from]. */
    data class OffGapNotice(val from: Instant, val to: Instant, val closed: Boolean) : PhoneSessionEffect
}
