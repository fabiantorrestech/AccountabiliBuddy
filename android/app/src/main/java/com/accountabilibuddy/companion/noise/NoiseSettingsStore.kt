package com.accountabilibuddy.companion.noise

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

/** Keeps [NoiseSettings] in the app's preferences (this phone only). */
class NoiseSettingsStore(context: Context) {
    private val prefs = context.getSharedPreferences("noise", Context.MODE_PRIVATE)
    private val _settings = MutableStateFlow(read())

    val settings: StateFlow<NoiseSettings> = _settings

    fun save(settings: NoiseSettings) {
        prefs.edit()
            .putString(FOLDER, settings.folderUri)
            .putInt(VOLUME, settings.volume)
            .putBoolean(REPEAT, settings.repeat)
            .putString(LAST, settings.lastFile)
            .putBoolean(SYSTEM, settings.systemControls)
            .putBoolean(WIDGET, settings.widgetButton)
            .putBoolean(WIDGET_VOLUME, settings.widgetVolume)
            .putBoolean(LOCK, settings.headphoneLock)
            .putBoolean(STOP_ON_DISCONNECT, settings.stopOnDisconnect)
            .putBoolean(PAUSE_DURING_CALLS, settings.pauseDuringCalls)
            .apply()
        _settings.value = settings
    }

    private fun read() = NoiseSettings(
        folderUri = prefs.getString(FOLDER, null),
        volume = prefs.getInt(VOLUME, 40),
        repeat = prefs.getBoolean(REPEAT, true),
        lastFile = prefs.getString(LAST, null),
        systemControls = prefs.getBoolean(SYSTEM, false),
        widgetButton = prefs.getBoolean(WIDGET, true),
        widgetVolume = prefs.getBoolean(WIDGET_VOLUME, true),
        headphoneLock = prefs.getBoolean(LOCK, false),
        stopOnDisconnect = prefs.getBoolean(STOP_ON_DISCONNECT, false),
        pauseDuringCalls = prefs.getBoolean(PAUSE_DURING_CALLS, true),
    )

    private companion object {
        const val FOLDER = "folder"
        const val VOLUME = "volume"
        const val REPEAT = "repeat"
        const val LAST = "last"
        const val SYSTEM = "systemControls"
        const val WIDGET = "widgetButton"
        const val WIDGET_VOLUME = "widgetVolume"
        const val LOCK = "headphoneLock"
        const val STOP_ON_DISCONNECT = "stopOnDisconnect"
        const val PAUSE_DURING_CALLS = "pauseDuringCalls"
    }
}
