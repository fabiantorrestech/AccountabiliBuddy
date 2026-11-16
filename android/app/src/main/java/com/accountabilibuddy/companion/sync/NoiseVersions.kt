package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncDeviceCard

/** Which apps answer a play or pause for their noise player (ADR 92), by the version on their card. Pure. */
object NoiseVersions {
    /** The first PC version that answers play and pause (ADR 92). */
    const val PC_PLAY_PAUSE = "0.1.147"

    /** The first phone version that writes its noise player and answers noise commands (ADR 92). */
    const val PHONE_PLAY_PAUSE = "0.1.82"

    /** True when the device's app answers play and pause. */
    fun supportsPlayPause(card: SyncDeviceCard): Boolean =
        compare(card.appVersion, if (card.kind == SyncDeviceCard.KIND_PC) PC_PLAY_PAUSE else PHONE_PLAY_PAUSE) >= 0

    // Dotted versions number by number ("0.1.107" > "0.1.99"); a part that isn't a number counts as 0 (as PcFileControl.compareVersions).
    private fun compare(a: String, b: String): Int {
        val left = a.substringBefore('+').substringBefore('-').split('.').map { it.toIntOrNull() ?: 0 }
        val right = b.substringBefore('+').substringBefore('-').split('.').map { it.toIntOrNull() ?: 0 }
        for (i in 0 until maxOf(left.size, right.size)) {
            val difference = left.getOrElse(i) { 0 } - right.getOrElse(i) { 0 }
            if (difference != 0) return difference
        }
        return 0
    }
}
