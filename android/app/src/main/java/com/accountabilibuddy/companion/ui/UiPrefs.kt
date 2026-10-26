package com.accountabilibuddy.companion.ui

import android.content.Context
import androidx.compose.animation.core.FiniteAnimationSpec
import androidx.compose.animation.core.snap
import androidx.compose.animation.core.tween
import androidx.compose.runtime.staticCompositionLocalOf
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

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

/** Light, dark, or as the phone is set. */
enum class ThemeChoice(val label: String) { System("Phone's setting"), Light("Light"), Dark("Dark") }

/** The phone app's look (preferences "ui"): animation speed and theme. Loaded once at app start. */
object UiPrefs {
    private val _motion = MutableStateFlow(MotionSpeed.Normal)
    private val _theme = MutableStateFlow(ThemeChoice.System)
    private val _barLook = MutableStateFlow(BarLook.Color)

    val barLook: StateFlow<BarLook> = _barLook.asStateFlow()

    val motion: StateFlow<MotionSpeed> = _motion.asStateFlow()
    val theme: StateFlow<ThemeChoice> = _theme.asStateFlow()

    fun load(context: Context) {
        val prefs = context.getSharedPreferences("ui", Context.MODE_PRIVATE)
        _motion.value = runCatching { MotionSpeed.valueOf(prefs.getString("motion", null) ?: "") }.getOrDefault(MotionSpeed.Normal)
        _theme.value = runCatching { ThemeChoice.valueOf(prefs.getString("theme", null) ?: "") }.getOrDefault(ThemeChoice.System)
        _barLook.value = runCatching { BarLook.valueOf(prefs.getString("barLook", null) ?: "") }.getOrDefault(BarLook.Color)
    }

    fun setBarLook(context: Context, look: BarLook) {
        _barLook.value = look
        context.getSharedPreferences("ui", Context.MODE_PRIVATE).edit().putString("barLook", look.name).apply()
    }

    fun setMotion(context: Context, speed: MotionSpeed) {
        _motion.value = speed
        context.getSharedPreferences("ui", Context.MODE_PRIVATE).edit().putString("motion", speed.name).apply()
    }

    fun setTheme(context: Context, theme: ThemeChoice) {
        _theme.value = theme
        context.getSharedPreferences("ui", Context.MODE_PRIVATE).edit().putString("theme", theme.name).apply()
    }
}

/** The animation speed in use below the theme (1 = normal, 0 = off). */
val LocalMotionScale = staticCompositionLocalOf { 1f }

/** A tween of [normalMs] at the chosen speed, or no animation when off. */
fun <T> motionSpec(scale: Float, normalMs: Int): FiniteAnimationSpec<T> =
    if (scale <= 0f) snap() else tween((normalMs * scale).toInt())
