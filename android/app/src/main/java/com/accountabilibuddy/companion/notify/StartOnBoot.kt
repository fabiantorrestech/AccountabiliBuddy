package com.accountabilibuddy.companion.notify

import android.content.Context

/**
 * "Start when the phone turns on" (Settings, on by default): at boot the app pins its timeline card again, which keeps
 * it running for the nudges, the work card and syncing. Off: it waits until it's opened.
 */
object StartOnBoot {
    private const val PREFS = "startup"
    private const val KEY = "boot"

    fun isOn(context: Context): Boolean = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean(KEY, true)

    fun set(context: Context, on: Boolean) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putBoolean(KEY, on).apply()
    }
}
