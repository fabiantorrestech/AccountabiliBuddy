package com.accountabilibuddy.companion.noise

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** A paired Bluetooth device chosen to start the noise: its address and name. */
data class ChosenHeadphones(val address: String, val name: String)

/**
 * "Play when headphones connect" (prefs "headphones"): wired on/off and Bluetooth on/off with the chosen devices,
 * all off by default. With Bluetooth on and no device chosen, nothing happens.
 */
data class HeadphoneSettings(val wired: Boolean = false, val bluetooth: Boolean = false, val devices: List<ChosenHeadphones> = emptyList())

class HeadphoneStore(context: Context) {
    private val prefs = context.getSharedPreferences("headphones", Context.MODE_PRIVATE)
    private val _settings = MutableStateFlow(read())
    val settings: StateFlow<HeadphoneSettings> = _settings.asStateFlow()

    fun save(settings: HeadphoneSettings) {
        prefs.edit()
            .putBoolean(KEY_WIRED, settings.wired)
            .putBoolean(KEY_BLUETOOTH, settings.bluetooth)
            .putStringSet(KEY_DEVICES, settings.devices.map { "${it.address}$SEPARATOR${it.name}" }.toSet())
            .apply()
        _settings.value = settings
    }

    private fun read() = HeadphoneSettings(
        wired = prefs.getBoolean(KEY_WIRED, false),
        bluetooth = prefs.getBoolean(KEY_BLUETOOTH, false),
        devices = prefs.getStringSet(KEY_DEVICES, emptySet()).orEmpty().mapNotNull { entry ->
            val parts = entry.split(SEPARATOR, limit = 2)
            if (parts.size == 2) ChosenHeadphones(parts[0], parts[1]) else null
        }.sortedBy { it.name.lowercase() },
    )

    private companion object {
        const val KEY_WIRED = "wired"
        const val KEY_BLUETOOTH = "bluetooth"
        const val KEY_DEVICES = "devices"
        const val SEPARATOR = "|"
    }
}
