package com.accountabilibuddy.shared.sync

import java.time.Instant

/**
 * What a device's noise player is doing, as its file says (ADR 42, 36; a phone's too since ADR 92), as on the PC (Core's
 * SyncNoise). Absent when the device has no sound files to play.
 *
 * @property name The shown name of the current file; never a path.
 * @property volume 0.0 to 1.0.
 * @property locked The headphone lock is on and blocks play right now.
 * @property at When the device looked (a phone: when its player last changed), on its clock.
 */
data class SyncNoise(val playing: Boolean, val name: String?, val volume: Double, val locked: Boolean, val at: Instant)

/**
 * The ops of a [SyncAction.NOISE] action's `{ "op", "value"? }` (ADR 42, 91), as on the PC (Core's SyncNoiseRequest). [PLAY] and
 * [PAUSE] (ADR 92) say which way, so a command that waits for a slow sync can't flip the player the wrong way; an app older than
 * them answers "unsupported".
 */
object SyncNoiseOps {
    const val TOGGLE = "toggle"
    const val NEXT = "next"
    const val PREV = "prev"
    const val VOLUME = "volume"
    const val PLAY = "play"
    const val PAUSE = "pause"
}
