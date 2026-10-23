package com.accountabilibuddy.companion.noise

/**
 * The phone's noise player choices.
 * @property folderUri The sounds folder (a SAF tree uri the app keeps access to); null until one is chosen.
 * @property volume The player's own volume, 0 to 100 (40 by default, as on the PC).
 * @property repeat Repeat the current sound (on by default); off plays the next one when it ends.
 * @property systemControls Play as media: Android's media controls drive it and it pauses for other audio. Off (the
 * default): no media session and no audio focus, so other audio such as a YouTube video plays over it.
 * @property widgetButton Play/pause on the home-screen widget (on by default).
 */
data class NoiseSettings(
    val folderUri: String? = null,
    val volume: Int = 40,
    val repeat: Boolean = true,
    val lastFile: String? = null,
    val systemControls: Boolean = false,
    val widgetButton: Boolean = true,
)
