package com.accountabilibuddy.companion.session

import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.session.PhoneSessionRefusal

/** What [PhoneSessionController.start] gave. */
sealed interface PhoneSessionStart {
    data class Started(val record: PhoneSessionRecord) : PhoneSessionStart

    /** A phone session already runs (its own, or a test). */
    data object AlreadyRunning : PhoneSessionStart

    /** R1: another device of the group has a live session. */
    data object LiveElsewhere : PhoneSessionStart

    /** The checkpoint could not be saved, so nothing started (a session that can't survive the process dying is not started). */
    data object StoreFailed : PhoneSessionStart

    /** The engine said no (a bad length). */
    data class Refused(val reason: PhoneSessionRefusal) : PhoneSessionStart
}
