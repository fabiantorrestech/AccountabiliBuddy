package com.accountabilibuddy.shared.sync

import java.time.Duration

/** How fresh another device's part is on this one (ADR 22), as the Devices tab's pill says it. */
enum class SyncState(val label: String) {
    UP_TO_DATE("Up to date"),
    BEHIND("Behind"),
    WAITING("Waiting for first sync"),
    UNLINKED("Unlinked");

    companion object {
        /** A device's part counts as behind once it's older than its interval plus this margin. */
        val MARGIN: Duration = Duration.ofMinutes(5)

        /** From the age of the newest file from a device ([age] null: none yet) and how often it should come. */
        fun of(age: Duration?, interval: Duration): SyncState = when {
            age == null -> WAITING
            age > interval + MARGIN -> BEHIND
            else -> UP_TO_DATE
        }
    }
}
