package com.accountabilibuddy.shared.session

/** How a phone session ended (the 90-day log's end kind, R6: only the phone ends it). */
enum class PhoneSessionEndKind {
    /** The set end passed. */
    TIME_UP,

    /** AccountabiliBuddy: ended early after the wait and a reason. */
    EARLY_END,

    /** "All tasks done" (R9, ADR 25). */
    ALL_TASKS_DONE,

    /** A session that ends on command (no set end, a test) or ProductaBuddy's End. */
    ON_COMMAND,

    /** The phone was off long enough (or across a day) that the session closed at the moment it went off (R13). */
    PHONE_OFF,
}
