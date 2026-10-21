package com.accountabilibuddy.companion.link

/** The link protocol's message types (ADR 14), as on the PC (Core/Link/LinkMessages.cs). */
object LinkMessages {
    const val PROTOCOL_VERSION = 1

    // Device to PC.
    const val PAIR = "pair"
    const val HELLO = "hello"
    const val START = "start"
    const val END_EARLY_BEGIN = "endEarlyBegin"
    const val END_EARLY = "endEarly"
    const val UNPAIR = "unpair"
    const val DAY_PLAN_EDIT = "dayPlanEdit"

    // PC to device.
    const val PAIRED = "paired"
    const val WELCOME = "welcome"
    const val STATE = "state"
    const val SETTINGS = "settings"
    const val END_EARLY_TERMS = "endEarlyTerms"
    const val RESULT = "result"
    const val DAY_PLAN_ANSWER = "dayPlanAnswer"
    const val REFUSED = "refused"
}
