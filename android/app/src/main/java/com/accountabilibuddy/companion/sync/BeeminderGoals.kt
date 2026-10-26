package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncBeeminderGoal
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import java.time.Instant

/**
 * The user's Beeminder goals (ADR 43) as a PC read them, and which PC; shown read-only on Today. They come only in a
 * PC's sealed sync file: the phone has no token and opens no connection for them.
 */
data class BeeminderGoals(val source: String, val goals: List<SyncBeeminderGoal>, val writtenAt: Instant) {
    companion object {
        /** The newest PC file that carries goals; null when none does (no token on any PC, or removed). */
        fun of(received: List<ReceivedDevice>, now: Instant): BeeminderGoals? =
            received
                .filter { it.file.device.kind == SyncDeviceCard.KIND_PC && !it.file.beeminder.isNullOrEmpty() && !it.file.device.writtenAt.isAfter(now.plusSeconds(60)) }
                .maxByOrNull { it.file.device.writtenAt }
                ?.let { device -> BeeminderGoals(device.file.device.name, device.file.beeminder!!.sortedBy { it.dueAt }, device.file.device.writtenAt) }
    }
}
