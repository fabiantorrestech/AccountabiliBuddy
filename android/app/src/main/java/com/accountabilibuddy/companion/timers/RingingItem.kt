package com.accountabilibuddy.companion.timers

/** A timer or alarm that's ringing now: which one, and what the ring screen and its notification say. */
data class RingingItem(val kind: Kind, val id: Int, val title: String, val text: String) {
    enum class Kind { TIMER, ALARM }

    fun isSame(kind: Kind, id: Int): Boolean = this.kind == kind && this.id == id
}
