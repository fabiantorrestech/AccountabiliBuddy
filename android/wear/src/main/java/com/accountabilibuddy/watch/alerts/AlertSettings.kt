package com.accountabilibuddy.watch.alerts

import android.content.Context

/** How strongly the watch buzzes when a focus or break phase ends. */
enum class Vibration(val label: String) { Off("Off"), Light("Light"), Medium("Medium"), Strong("Strong") }

/**
 * The watch's alert choices (kept on the watch): the vibration's strength (medium by default), a sound with it (off),
 * and a short buzz a minute before the end (on).
 */
object AlertSettings {
    private const val PREFS = "alerts"
    private const val VIBRATION = "vibration"
    private const val SOUND = "sound"
    private const val WARNING = "warning"

    fun vibration(context: Context): Vibration =
        runCatching { Vibration.valueOf(prefs(context).getString(VIBRATION, null) ?: Vibration.Medium.name) }.getOrDefault(Vibration.Medium)

    fun sound(context: Context): Boolean = prefs(context).getBoolean(SOUND, false)

    fun warning(context: Context): Boolean = prefs(context).getBoolean(WARNING, true)

    fun setVibration(context: Context, vibration: Vibration) = prefs(context).edit().putString(VIBRATION, vibration.name).apply()

    fun setSound(context: Context, on: Boolean) = prefs(context).edit().putBoolean(SOUND, on).apply()

    fun setWarning(context: Context, on: Boolean) = prefs(context).edit().putBoolean(WARNING, on).apply()

    private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
}
