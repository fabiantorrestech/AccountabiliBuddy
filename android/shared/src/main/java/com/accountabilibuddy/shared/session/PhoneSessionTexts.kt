package com.accountabilibuddy.shared.session

/** Words of the phone session that more than one screen says (PS3a). Pure, so a check pins them. */
object PhoneSessionTexts {
    /**
     * The mode-switch dialog's line about the session's locks (PS2c review N4). A PC session keeps its locks either way. A session on this phone does not:
     * switching "now" (paid for) leaves ProductaBuddy's rules, which lock only what the session's profile turns on, and ending the session is free from then.
     */
    fun modeSwitchLocksLine(phoneSession: Boolean): String =
        if (phoneSession) {
            "Switching after this session ends keeps all its locks until then. Switching now leaves only what this session's profile turns on, and ending the session becomes free."
        } else {
            "The session keeps its locks either way; what changes is that nothing else nudges or locks."
        }

    /** Why the engine said no to a Session menu action, in words for the row or a message (never a reason the user typed). */
    fun refusal(reason: PhoneSessionRefusal): String = when (reason) {
        PhoneSessionRefusal.NOT_RUNNING -> "No session is running."
        PhoneSessionRefusal.ALREADY_AWAY -> "Come back or resume first."
        PhoneSessionRefusal.NOT_AWAY, PhoneSessionRefusal.NOT_STEPPED_AWAY -> "You are not away."
        PhoneSessionRefusal.NOT_PAUSED -> "The timer is not paused."
        PhoneSessionRefusal.IS_TEST -> "Not for a test session."
        PhoneSessionRefusal.NO_SET_END -> "A session with no set end has nothing to add to."
        PhoneSessionRefusal.TIME_IS_UP -> "The time is up."
        PhoneSessionRefusal.TOO_LONG -> "A session can be 12 hours long at most."
        PhoneSessionRefusal.BAD_LENGTH -> "A session can be 1 minute to 12 hours long."
        PhoneSessionRefusal.REASON_TOO_SHORT -> "The reason is too short."
        PhoneSessionRefusal.NOT_ALL_DONE -> "Not every task is done yet."
        PhoneSessionRefusal.NOT_ARMED -> "Nothing is waiting to end."
        PhoneSessionRefusal.ENDS_ON_COMMAND_ONLY -> "This session ends with the wait and a reason."
    }

    /** The pause section's line for a ProductaBuddy phone session, whose locks the pause would drop (Q1207): the request waits until the session ends. */
    const val PB_SESSION_RESTS = "A session on this phone is running: a change to the pause waits until it ends."
}
