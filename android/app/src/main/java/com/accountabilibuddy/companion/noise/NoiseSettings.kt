package com.accountabilibuddy.companion.noise

/**
 * The phone's noise player choices.
 * @property folderUri The sounds folder (a SAF tree uri the app keeps access to); null until one is chosen.
 * @property volume The player's own volume, 0 to 100 (40 by default, as on the PC).
 * @property repeat Repeat the current sound (on by default); off plays the next one when it ends.
 * @property systemControls Play as media: Android's media controls drive it and it pauses for other audio. Off (the
 * default): no media session and no audio focus, so other audio such as a YouTube video plays over it.
 * @property widgetButton Play/pause on the home-screen widget (on by default).
 * @property widgetVolume Volume down and up (10 at a time, as the PC's shortcuts) on the widget (on by default).
 * @property headphoneLock The lock on the player (off by default): it plays only while headphones are connected, and
 * pauses when the last ones go.
 * @property stopOnDisconnect Pause when headphones disconnect, locked or not (off by default; separate from "Play when
 * headphones connect").
 * @property pauseDuringCalls Pause while a call rings or is active and resume after it if it was playing (on by default).
 */
data class NoiseSettings(
    val folderUri: String? = null,
    val volume: Int = 40,
    val repeat: Boolean = true,
    val lastFile: String? = null,
    val systemControls: Boolean = false,
    val widgetButton: Boolean = true,
    val widgetVolume: Boolean = true,
    val headphoneLock: Boolean = false,
    val stopOnDisconnect: Boolean = false,
    val pauseDuringCalls: Boolean = true,
)
