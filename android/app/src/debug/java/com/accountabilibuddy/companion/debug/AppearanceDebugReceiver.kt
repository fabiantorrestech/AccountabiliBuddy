package com.accountabilibuddy.companion.debug

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import com.accountabilibuddy.companion.ui.UiPrefs
import com.accountabilibuddy.companion.ui.theme.AppearancePlace
import com.accountabilibuddy.companion.ui.theme.ColourMode
import com.accountabilibuddy.companion.ui.theme.GeoPoint
import com.accountabilibuddy.companion.ui.theme.LightDarkMode
import com.accountabilibuddy.companion.ui.theme.ThemeName
import java.time.LocalTime

/**
 * Debug only: sets the appearance preferences from adb, so the screenshots can walk through the six palettes. Extras (all
 * optional): theme (instrument, nightdesk, paper), mode (system, light, dark, settime, sunset), colour (quiet, standard),
 * darkat and lightat (HH:mm), place ("lat,lon", or "none"), legacy (Light, Dark or System: writes the pre-theme "theme"
 * key and removes "mode", then the next app start migrates it).
 */
class AppearanceDebugReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        intent.getStringExtra("legacy")?.let { old ->
            context.getSharedPreferences("ui", Context.MODE_PRIVATE).edit().putString("theme", old).remove("mode").apply()
            Log.i("AppearanceDebug", "legacy theme=$old written, mode removed; restart the app")
            return
        }
        intent.getStringExtra("theme")?.let { name ->
            val theme = ThemeName.entries.firstOrNull { it.name.equals(name, ignoreCase = true) } ?: return@let
            UiPrefs.setThemeName(context, theme)
        }
        intent.getStringExtra("mode")?.let { name ->
            val mode = LightDarkMode.entries.firstOrNull { it.name.equals(name, ignoreCase = true) } ?: return@let
            UiPrefs.setMode(context, mode)
        }
        intent.getStringExtra("colour")?.let { name ->
            val colour = ColourMode.entries.firstOrNull { it.name.equals(name, ignoreCase = true) } ?: return@let
            UiPrefs.setColour(context, colour)
        }
        val darkAt = intent.getStringExtra("darkat")?.let(LocalTime::parse)
        val lightAt = intent.getStringExtra("lightat")?.let(LocalTime::parse)
        if (darkAt != null || lightAt != null) {
            UiPrefs.setSetTimes(context, darkAt ?: UiPrefs.darkAt.value, lightAt ?: UiPrefs.lightAt.value)
        }
        intent.getStringExtra("place")?.let { text ->
            if (text == "none") {
                UiPrefs.setPlace(context, null)
            } else {
                val parts = text.split(',')
                UiPrefs.setPlace(context, AppearancePlace(GeoPoint(parts[0].toDouble(), parts[1].toDouble()), "Typed in"))
            }
        }
        Log.i("AppearanceDebug", "theme=${UiPrefs.themeName.value} mode=${UiPrefs.mode.value} colour=${UiPrefs.colour.value}")
    }
}
