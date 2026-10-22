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
    const val FOCUS_DAY = "focusDay"
    const val END_EARLY_TERMS = "endEarlyTerms"
    const val RESULT = "result"
    const val DAY_PLAN_ANSWER = "dayPlanAnswer"
    const val REFUSED = "refused"

    // Both ways. A sealed sync file (ADR 22), body {"data": base64}: the PC sends its own and each one it received;
    // the phone sends the files it carries, and the PC answers each with RESULT.
    const val SYNC_FILE = "syncFile"
}
