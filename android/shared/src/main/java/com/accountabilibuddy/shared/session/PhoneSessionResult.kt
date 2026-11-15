package com.accountabilibuddy.shared.session

/** What a transition of [PhoneSessionEngine] gives back. */
sealed interface PhoneSessionResult {
    /** The new record and what to do about it. A transition that changes nothing returns the same record and no effects. */
    data class Done(val record: PhoneSessionRecord, val effects: List<PhoneSessionEffect> = emptyList()) : PhoneSessionResult

    /** The record stays as it was. */
    data class Refused(val reason: PhoneSessionRefusal) : PhoneSessionResult
}
