package com.accountabilibuddy.shared.sync

import java.time.Instant

/**
 * What a PC's noise player is doing, as its file says (ADR 42, 36), as on the PC (Core's SyncNoise). Absent when the PC
 * has no sound files to play.
 *
 * @property name The shown name of the current file; never a path.
 * @property volume 0.0 to 1.0.
 * @property locked The headphone lock is on and blocks play right now.
 * @property at When the PC looked, on its clock.
 */
data class SyncNoise(val playing: Boolean, val name: String?, val volume: Double, val locked: Boolean, val at: Instant)
