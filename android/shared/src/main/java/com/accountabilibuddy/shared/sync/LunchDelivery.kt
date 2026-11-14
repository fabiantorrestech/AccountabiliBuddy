package com.accountabilibuddy.shared.sync

import java.time.Duration
import java.time.Instant

/** Another active device's latest received file, as far as the lunch goes: its name, when it wrote the file, and the lunch in it (null: none). */
data class LunchPeer(val name: String, val writtenAt: Instant, val lunch: SyncLunch?)

/**
 * Whether the other devices have this device's lunch, from the latest file received from each (the PC's LunchDelivery is the same rule).
 * A device has it when its file holds the same pieces (current piece, counted pieces, allowance), or a newer edit. A device whose file is older than [ABSENT_AFTER] is off
 * or not syncing, and is left out rather than reported on all day; the caller leaves out watches (they write no file) and removed devices.
 */
object LunchDelivery {
    val ABSENT_AFTER: Duration = Duration.ofHours(12)

    /** The devices that do not have [ours] yet, sorted by name; empty when all have it or there is no other device. */
    fun missing(ours: SyncLunch, peers: List<LunchPeer>, now: Instant): List<LunchPeer> =
        peers.filter { present(it, now) && !has(it.lunch, ours) }.sortedBy { it.name.lowercase() }

    /** The card's line: "On all your devices", "Not on Laptop yet · last heard 12:03 PM" ([time] makes the 12-hour text), or null with no other device. */
    fun line(ours: SyncLunch, peers: List<LunchPeer>, now: Instant, time: (Instant) -> String): String? {
        if (peers.none { present(it, now) }) {
            return null
        }

        val missing = missing(ours, peers, now)
        if (missing.isEmpty()) {
            return "On all your devices"
        }

        val first = missing.first()
        val names = if (missing.size == 1) first.name else "${first.name} and ${missing.size - 1} more"
        return "Not on $names yet · last heard ${time(first.writtenAt)}"
    }

    private fun present(peer: LunchPeer, now: Instant): Boolean = Duration.between(peer.writtenAt, now) <= ABSENT_AFTER

    private fun has(theirs: SyncLunch?, ours: SyncLunch): Boolean =
        theirs != null && (LunchDay.hasPieces(theirs, ours) || theirs.lastEdit > ours.lastEdit)
}
