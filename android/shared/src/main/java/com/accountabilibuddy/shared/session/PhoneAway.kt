package com.accountabilibuddy.shared.session

import java.time.Instant

/**
 * One stretch of a phone session the user was not there for (ADR 85 R7, ADR 77, ADR 60). Every kind is out of focus time (it is cut
 * out of the pieces other devices count); they differ in what the timer does.
 *
 * @property pausesTimer The countdown freezes: on the way back the phase's start and end move later by the stretch. Step away keeps the
 * timer running (ADR 77: "I'm back" moves nothing).
 */
enum class PhoneAwayKind(val pausesTimer: Boolean) {
    /** Step away: the timer keeps running, enforcement unchanged. */
    STEP_AWAY(false),

    /** Pause timer: the countdown freezes, enforcement unchanged. */
    PAUSED(true),

    /** Lunch started on any device: pauses like Pause timer, from the lunch's start to its end (ADR 88 pieces follow the lunch's choice). */
    LUNCH(true),

    /** The phone was off or the app closed (ADR 60, R13): out of focus, the timer paused. Sent as `offMinutesBefore`, never why. */
    DEVICE_OFF(true),
}

/** A stretch away from [from] to [to]; [to] is null while it is still going. */
data class PhoneAway(val kind: PhoneAwayKind, val from: Instant, val to: Instant?) {
    val isOpen: Boolean get() = to == null
}
