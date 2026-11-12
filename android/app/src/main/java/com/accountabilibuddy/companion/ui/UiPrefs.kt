package com.accountabilibuddy.companion.ui

import android.content.Context
import android.content.SharedPreferences
import androidx.compose.animation.core.FiniteAnimationSpec
import androidx.compose.animation.core.snap
import androidx.compose.animation.core.tween
import androidx.compose.runtime.staticCompositionLocalOf
import com.accountabilibuddy.companion.today.TodayLayout
import com.accountabilibuddy.companion.ui.theme.AppearancePlace
import com.accountabilibuddy.companion.ui.theme.ColourMode
import com.accountabilibuddy.companion.ui.theme.LightDarkMode
import com.accountabilibuddy.companion.ui.theme.ThemeName
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.time.LocalTime

/** How fast the app's animations run; Off shows every change at once. */
enum class MotionSpeed(val label: String, val scale: Float) {
    Normal("Normal", 1f),
    Fast("Fast", 0.6f),
    Slow("Slow", 1.7f),
    Off("Off", 0f),
}

/**
 * How the timeline is drawn on the widget, the notification and Today: in colour; in grays with every block outlined
 * (a phone kept in grayscale); or black and white with no see-through layers (e-ink screens).
 */
enum class BarLook(val label: String) { Color("Colour"), Grayscale("Grayscale (outlined)"), Eink("E-ink (black and white)") }

/**
 * The phone app's look (preferences "ui"): theme, light or dark, colour, the set times and the After sunset place, plus
 * animation speed, timeline look and whether the sync light shows. Per device and never synced (ADR 49). Loaded once at app start.
 */
object UiPrefs {
    private const val FILE = "ui"

    private val _motion = MutableStateFlow(MotionSpeed.Normal)
    private val _themeName = MutableStateFlow(ThemeName.Instrument)
    private val _mode = MutableStateFlow(LightDarkMode.System)
    private val _colour = MutableStateFlow(ColourMode.Quiet)
    private val _darkAt = MutableStateFlow(LocalTime.of(19, 0))
    private val _lightAt = MutableStateFlow(LocalTime.of(7, 0))
    private val _place = MutableStateFlow<AppearancePlace?>(null)
    private val _barLook = MutableStateFlow(BarLook.Color)
    private val _syncLight = MutableStateFlow(SyncLightSetting.On)
    private val _todayLayout = MutableStateFlow(TodayLayout())
    private val _todaySessionLayout = MutableStateFlow<TodayLayout?>(null)

    val barLook: StateFlow<BarLook> = _barLook.asStateFlow()

    /** The order of Today's parts and where each sits (Settings › Appearance › Today layout); this phone only, never synced. */
    val todayLayout: StateFlow<TodayLayout> = _todayLayout.asStateFlow()

    /**
     * Today's own layout while a session runs (round 2, Settings › Today: "In a session"); null until the person sets one, and then
     * a session uses [todayLayout] as it always did. This phone only, never synced.
     */
    val todaySessionLayout: StateFlow<TodayLayout?> = _todaySessionLayout.asStateFlow()

    /** Whether Today shows the sync light (Settings › Devices & sync). It only hides the light: syncing itself is never touched. */
    val syncLight: StateFlow<SyncLightSetting> = _syncLight.asStateFlow()
    val motion: StateFlow<MotionSpeed> = _motion.asStateFlow()
    val themeName: StateFlow<ThemeName> = _themeName.asStateFlow()
    val mode: StateFlow<LightDarkMode> = _mode.asStateFlow()
    val colour: StateFlow<ColourMode> = _colour.asStateFlow()

    /** Dark from this time of day (At a set time, and After sunset when the sun can't be worked out). */
    val darkAt: StateFlow<LocalTime> = _darkAt.asStateFlow()
    val lightAt: StateFlow<LocalTime> = _lightAt.asStateFlow()

    /** The stored place for After sunset, or null. Private: show it only in Settings → Appearance. */
    val place: StateFlow<AppearancePlace?> = _place.asStateFlow()

    fun load(context: Context) {
        val prefs = prefs(context)
        _motion.value = runCatching { MotionSpeed.valueOf(prefs.getString("motion", null) ?: "") }.getOrDefault(MotionSpeed.Normal)
        _barLook.value = runCatching { BarLook.valueOf(prefs.getString("barLook", null) ?: "") }.getOrDefault(BarLook.Color)
        _todayLayout.value = TodayLayout.parse(prefs.getString("todayOrder", null), prefs.getString("todayPlacements", null))
        _todaySessionLayout.value = if (prefs.getBoolean("todaySessionOwn", false)) {
            TodayLayout.parse(prefs.getString("todaySessionOrder", null), prefs.getString("todaySessionPlacements", null))
        } else {
            null
        }
        _syncLight.value = runCatching { SyncLightSetting.valueOf(prefs.getString("syncLight", null) ?: "") }.getOrDefault(SyncLightSetting.On)
        _themeName.value = runCatching { ThemeName.valueOf(prefs.getString("themeName", null) ?: "") }.getOrDefault(ThemeName.Instrument)
        _colour.value = ColourMode.fromCode(prefs.getInt("colour", ColourMode.Quiet.code))
        _darkAt.value = LocalTime.ofSecondOfDay(prefs.getInt("darkAtMin", 19 * 60).coerceIn(0, 1439) * 60L)
        _lightAt.value = LocalTime.ofSecondOfDay(prefs.getInt("lightAtMin", 7 * 60).coerceIn(0, 1439) * 60L)
        _place.value = AppearancePlace.open(prefs.getString("place", null))
        _mode.value = if (prefs.contains("mode")) LightDarkMode.fromCode(prefs.getInt("mode", 0)) else migrateOldTheme(prefs)
    }

    // Before the themes, "theme" held System, Light or Dark. That becomes the light/dark mode (the look is Instrument).
    private fun migrateOldTheme(prefs: SharedPreferences): LightDarkMode {
        val mode = when (prefs.getString("theme", null)) {
            "Light" -> LightDarkMode.Light
            "Dark" -> LightDarkMode.Dark
            else -> LightDarkMode.System
        }
        prefs.edit().putInt("mode", mode.code).remove("theme").apply()
        return mode
    }

    fun setSyncLight(context: Context, setting: SyncLightSetting) {
        _syncLight.value = setting
        prefs(context).edit().putString("syncLight", setting.name).apply()
    }

    fun setTodayLayout(context: Context, layout: TodayLayout) {
        _todayLayout.value = layout
        val edit = prefs(context).edit()
        layout.orderText()?.let { edit.putString("todayOrder", it) } ?: edit.remove("todayOrder")
        layout.placementsText()?.let { edit.putString("todayPlacements", it) } ?: edit.remove("todayPlacements")
        edit.apply()
    }

    /** Sets Today's in-session layout; null goes back to using [todayLayout] in a session too. */
    fun setTodaySessionLayout(context: Context, layout: TodayLayout?) {
        _todaySessionLayout.value = layout
        val edit = prefs(context).edit()
        if (layout == null) {
            edit.remove("todaySessionOwn").remove("todaySessionOrder").remove("todaySessionPlacements")
        } else {
            edit.putBoolean("todaySessionOwn", true)
            layout.orderText()?.let { edit.putString("todaySessionOrder", it) } ?: edit.remove("todaySessionOrder")
            layout.placementsText()?.let { edit.putString("todaySessionPlacements", it) } ?: edit.remove("todaySessionPlacements")
        }
        edit.apply()
    }

    fun setBarLook(context: Context, look: BarLook) {
        _barLook.value = look
        prefs(context).edit().putString("barLook", look.name).apply()
    }

    fun setMotion(context: Context, speed: MotionSpeed) {
        _motion.value = speed
        prefs(context).edit().putString("motion", speed.name).apply()
    }

    fun setThemeName(context: Context, theme: ThemeName) {
        _themeName.value = theme
        prefs(context).edit().putString("themeName", theme.name).apply()
    }

    fun setMode(context: Context, mode: LightDarkMode) {
        _mode.value = mode
        prefs(context).edit().putInt("mode", mode.code).apply()
    }

    fun setColour(context: Context, colour: ColourMode) {
        _colour.value = colour
        prefs(context).edit().putInt("colour", colour.code).apply()
    }

    fun setSetTimes(context: Context, darkAt: LocalTime, lightAt: LocalTime) {
        _darkAt.value = darkAt
        _lightAt.value = lightAt
        prefs(context).edit()
            .putInt("darkAtMin", darkAt.hour * 60 + darkAt.minute)
            .putInt("lightAtMin", lightAt.hour * 60 + lightAt.minute)
            .apply()
    }

    /** Stores the place sealed, or forgets it (null). */
    fun setPlace(context: Context, place: AppearancePlace?) {
        _place.value = place
        val edit = prefs(context).edit()
        if (place == null) edit.remove("place") else edit.putString("place", place.seal())
        edit.apply()
    }

    private fun prefs(context: Context): SharedPreferences = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)
}

/** The animation speed in use below the theme (1 = normal, 0 = off). */
val LocalMotionScale = staticCompositionLocalOf { 1f }

/** A tween of [normalMs] at the chosen speed, or no animation when off. */
fun <T> motionSpec(scale: Float, normalMs: Int): FiniteAnimationSpec<T> =
    if (scale <= 0f) snap() else tween((normalMs * scale).toInt())
