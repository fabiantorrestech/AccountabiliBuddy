package com.accountabilibuddy.companion.noise

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.util.UUID

/** A paired Bluetooth device chosen to start the noise: its address and name. */
data class ChosenHeadphones(val address: String, val name: String)

/**
 * "Play when headphones connect" (prefs "headphones"): wired on/off and Bluetooth on/off with the chosen devices,
 * all off by default. With Bluetooth on and no device chosen, nothing happens.
 */
data class HeadphoneSettings(
    val wired: Boolean = false,
    val bluetooth: Boolean = false,
    val devices: List<ChosenHeadphones> = emptyList(),
    /** "Only at certain times": auto-play only when a ticked option below matches. Off = any time. */
    val onlyAtTimes: Boolean = false,
    val duringPlanWindow: Boolean = false,
    /** The plan categories that count; null = the plan's Work category (first Watch category). */
    val planCategoryIds: Set<UUID>? = null,
    val onDaysAndHours: Boolean = false,
    /** [java.time.DayOfWeek] values, 1 = Monday; Monday to Friday by default. */
    val days: Set<Int> = setOf(1, 2, 3, 4, 5),
    /** Minutes after midnight; 8:00 AM by default. */
    val startMinute: Int = 8 * 60,
    /** Minutes after midnight; 6:00 PM by default. */
    val endMinute: Int = 18 * 60,
)

class HeadphoneStore(context: Context) {
    private val prefs = context.getSharedPreferences("headphones", Context.MODE_PRIVATE)
    private val _settings = MutableStateFlow(read())
    val settings: StateFlow<HeadphoneSettings> = _settings.asStateFlow()

    fun save(settings: HeadphoneSettings) {
        prefs.edit()
            .putBoolean(KEY_WIRED, settings.wired)
            .putBoolean(KEY_BLUETOOTH, settings.bluetooth)
            .putStringSet(KEY_DEVICES, settings.devices.map { "${it.address}$SEPARATOR${it.name}" }.toSet())
            .putBoolean(KEY_ONLY_AT_TIMES, settings.onlyAtTimes)
            .putBoolean(KEY_PLAN_WINDOW, settings.duringPlanWindow)
            .putBoolean(KEY_DAYS_AND_HOURS, settings.onDaysAndHours)
            .putStringSet(KEY_DAYS, settings.days.map { it.toString() }.toSet())
            .putInt(KEY_START, settings.startMinute)
            .putInt(KEY_END, settings.endMinute)
            .apply {
                // Absent = the default Work category.
                val categories = settings.planCategoryIds
                if (categories == null) remove(KEY_CATEGORIES) else putStringSet(KEY_CATEGORIES, categories.map { it.toString() }.toSet())
            }
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
        onlyAtTimes = prefs.getBoolean(KEY_ONLY_AT_TIMES, false),
        duringPlanWindow = prefs.getBoolean(KEY_PLAN_WINDOW, false),
        planCategoryIds = if (prefs.contains(KEY_CATEGORIES)) {
            prefs.getStringSet(KEY_CATEGORIES, emptySet()).orEmpty().mapNotNull { runCatching { UUID.fromString(it) }.getOrNull() }.toSet()
        } else {
            null
        },
        onDaysAndHours = prefs.getBoolean(KEY_DAYS_AND_HOURS, false),
        days = if (prefs.contains(KEY_DAYS)) {
            prefs.getStringSet(KEY_DAYS, emptySet()).orEmpty().mapNotNull { it.toIntOrNull() }.toSet()
        } else {
            HeadphoneSettings().days
        },
        startMinute = prefs.getInt(KEY_START, 8 * 60),
        endMinute = prefs.getInt(KEY_END, 18 * 60),
    )

    private companion object {
        const val KEY_WIRED = "wired"
        const val KEY_BLUETOOTH = "bluetooth"
        const val KEY_DEVICES = "devices"
        const val KEY_ONLY_AT_TIMES = "onlyAtTimes"
        const val KEY_PLAN_WINDOW = "duringPlanWindow"
        const val KEY_CATEGORIES = "planCategoryIds"
        const val KEY_DAYS_AND_HOURS = "onDaysAndHours"
        const val KEY_DAYS = "days"
        const val KEY_START = "startMinute"
        const val KEY_END = "endMinute"
        const val SEPARATOR = "|"
    }
}
