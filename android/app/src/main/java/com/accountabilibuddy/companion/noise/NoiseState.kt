package com.accountabilibuddy.companion.noise

/** What the player bar and the widget show. */
data class NoiseState(
    val playing: Boolean = false,
    /** The current sound's shown name; null when there's none. */
    val title: String? = null,
    val hasFolder: Boolean = false,
    val fileCount: Int = 0,
    /** Any headphones connected now (wired or Bluetooth audio). */
    val headphonesConnected: Boolean = false,
    /** True for a few seconds after Play was refused by the lock: the bar says "Connect headphones". */
    val needsHeadphones: Boolean = false,
)
