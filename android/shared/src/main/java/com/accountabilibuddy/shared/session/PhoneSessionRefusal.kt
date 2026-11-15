package com.accountabilibuddy.shared.session

/** Why a transition did nothing (the record is unchanged). */
enum class PhoneSessionRefusal {
    NOT_RUNNING,
    ALREADY_AWAY,
    NOT_AWAY,
    NOT_PAUSED,
    NOT_STEPPED_AWAY,
    IS_TEST,
    NO_SET_END,
    TIME_IS_UP,
    TOO_LONG,
    BAD_LENGTH,
    REASON_TOO_SHORT,
    NOT_ALL_DONE,
    NOT_ARMED,
    ENDS_ON_COMMAND_ONLY,
}
