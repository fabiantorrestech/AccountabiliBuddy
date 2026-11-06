package com.accountabilibuddy.companion.guidance

import android.content.Context
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.noise.ChosenHeadphones
import com.accountabilibuddy.companion.noise.HeadphoneSettings
import com.accountabilibuddy.companion.ui.UiPrefs
import com.accountabilibuddy.companion.ui.theme.ColourMode
import com.accountabilibuddy.companion.ui.theme.LightDarkMode
import com.accountabilibuddy.companion.ui.theme.ThemeName
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.util.UUID

/**
 * One setting the phone keeps per mode (ADR 69), as text: [read] the value in effect, [write] one in, [format] how the compare view
 * shows it, [companionStart] ProductaBuddy's first value from AccountabiliBuddy's.
 */
class PhoneModeSetting(
    val id: String,
    val section: String,
    val label: String,
    val read: () -> String,
    val write: (String) -> Unit,
    val format: (String) -> String,
    val companionStart: (String) -> String = { it },
)

/**
 * The phone's settings kept per mode (ADR 69): the timeline notification, play when headphones connect, and the theme, light or
 * dark and colour. The values in effect stay where they always lived; when the mode switches, they are put aside for the mode
 * left and the other mode's are put in (preferences "modeSettings", never synced or backed up). ProductaBuddy starts with a copy of
 * AccountabiliBuddy's. The compare view reads and sets both sides.
 */
class PhoneModeSettings(private val app: CompanionApp) {
    private val prefs = app.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    val all: List<PhoneModeSetting> = listOf(
        PhoneModeSetting(
            "timelineNotification", "Alerts", "Timeline notification",
            read = { app.store.state.value.timelineNotificationOn.toString() },
            write = { app.store.setTimelineNotification(it.toBoolean()) },
            format = { if (it.toBoolean()) "Pinned" else "Off" },
        ),
        PhoneModeSetting(
            "headphones", "Alerts", "Play when headphones connect",
            read = { encode(app.headphones.settings.value) },
            write = { text -> decode(text)?.let(app.headphones::save) },
            format = { text -> decode(text)?.let(::describe) ?: "Off" },
        ),
        PhoneModeSetting(
            "theme", "Appearance", "Theme",
            read = { UiPrefs.themeName.value.name },
            write = { name -> runCatching { ThemeName.valueOf(name) }.getOrNull()?.let { UiPrefs.setThemeName(app, it) } },
            format = { name -> runCatching { ThemeName.valueOf(name).label }.getOrDefault(name) },
        ),
        PhoneModeSetting(
            "lightDark", "Appearance", "Light or dark",
            read = { UiPrefs.mode.value.code.toString() },
            write = { code -> code.toIntOrNull()?.let { UiPrefs.setMode(app, LightDarkMode.fromCode(it)) } },
            format = { code -> code.toIntOrNull()?.let { LightDarkMode.fromCode(it).label } ?: code },
        ),
        PhoneModeSetting(
            "colour", "Appearance", "Colour",
            read = { UiPrefs.colour.value.code.toString() },
            write = { code -> code.toIntOrNull()?.let { UiPrefs.setColour(app, ColourMode.fromCode(it)) } },
            format = { code -> code.toIntOrNull()?.let { ColourMode.fromCode(it).label } ?: code },
        ),
    )

    /** A setting whose two values differ: AccountabiliBuddy's and ProductaBuddy's, as text. */
    data class Row(val setting: PhoneModeSetting, val guarding: String, val companion: String)

    /**
     * Puts the values in effect aside for the mode left and puts the new mode's in. Call when the mode may have changed.
     * @return True when the mode switched (not on the first call, which only records it).
     */
    @Synchronized
    fun follow(mode: GuidanceMode): Boolean {
        val last = prefs.getString(KEY_ACTIVE, null)?.let { runCatching { GuidanceMode.valueOf(it) }.getOrNull() }
        if (last == mode) return false
        if (last == null) {
            prefs.edit().putString(KEY_ACTIVE, mode.name).apply()
            return false
        }

        // Crash-safe (ADR 69): first, durably, both sides (the values put aside and the ones to put in) and "switching"; then the
        // values go in; then "switching" is cleared. A crash in between is finished by [resumeInterrupted] at the next start.
        val edit = prefs.edit().putString(KEY_ACTIVE, mode.name).putString(KEY_SWITCHING, mode.name)
        val targets = mutableMapOf<PhoneModeSetting, String>()
        all.forEach { setting ->
            val current = runCatching { setting.read() }.getOrNull() ?: return@forEach
            edit.putString(key(setting, last), current)
            val next = prefs.getString(key(setting, mode), null)
                ?: if (mode == GuidanceMode.Companion) setting.companionStart(current) else current
            edit.putString(key(setting, mode), next)
            if (next != current) targets[setting] = next
        }
        edit.commit()
        targets.forEach { (setting, value) -> runCatching { setting.write(value) } }
        prefs.edit().remove(KEY_SWITCHING).commit()
        return true
    }

    /** At app start: a switch cut short by a crash or a kill puts the rest of its values in now (writing a value twice is harmless). */
    @Synchronized
    fun resumeInterrupted() {
        val mode = prefs.getString(KEY_SWITCHING, null)?.let { runCatching { GuidanceMode.valueOf(it) }.getOrNull() } ?: return
        all.forEach { setting ->
            val target = prefs.getString(key(setting, mode), null) ?: return@forEach
            if (runCatching { setting.read() }.getOrNull() != target) runCatching { setting.write(target) }
        }
        prefs.edit().remove(KEY_SWITCHING).commit()
    }

    /** The per-mode settings whose two values differ; a mode never used yet reads as a copy of the other. */
    fun compare(mode: GuidanceMode): List<Row> = all.mapNotNull { setting ->
        val current = runCatching { setting.read() }.getOrNull() ?: return@mapNotNull null
        val guarding = if (mode == GuidanceMode.Guarding) current else prefs.getString(key(setting, GuidanceMode.Guarding), null) ?: current
        val companion = if (mode == GuidanceMode.Companion) current else prefs.getString(key(setting, GuidanceMode.Companion), null) ?: current
        if (guarding == companion) null else Row(setting, guarding, companion)
    }

    /** Makes both modes keep the picked value of each row: AccountabiliBuddy's when the flag is true, else ProductaBuddy's. */
    @Synchronized
    fun apply(mode: GuidanceMode, picks: List<Pair<Row, Boolean>>) {
        val other = if (mode == GuidanceMode.Companion) GuidanceMode.Guarding else GuidanceMode.Companion
        val edit = prefs.edit()
        picks.forEach { (row, useGuarding) ->
            val value = if (useGuarding) row.guarding else row.companion
            runCatching { row.setting.write(value) }
            edit.putString(key(row.setting, other), value)
        }
        edit.apply()
    }

    private fun key(setting: PhoneModeSetting, mode: GuidanceMode) = "${setting.id}.${mode.name}"

    private fun describe(settings: HeadphoneSettings): String {
        val parts = buildList {
            if (settings.wired) add("wired")
            if (settings.bluetooth) add(if (settings.devices.isEmpty()) "Bluetooth (none chosen)" else "${settings.devices.size} Bluetooth")
        }
        if (parts.isEmpty()) return "Off"
        return parts.joinToString(", ").replaceFirstChar { it.uppercase() } + if (settings.onlyAtTimes) ", only at certain times" else ""
    }

    private fun encode(settings: HeadphoneSettings): String = buildJsonObject {
        put("wired", settings.wired)
        put("bluetooth", settings.bluetooth)
        put("devices", JsonArray(settings.devices.map { device -> buildJsonObject { put("address", device.address); put("name", device.name) } }))
        put("onlyAtTimes", settings.onlyAtTimes)
        put("duringPlanWindow", settings.duringPlanWindow)
        settings.planCategoryIds?.let { ids -> put("planCategoryIds", JsonArray(ids.map { JsonPrimitive(it.toString()) })) }
        put("onDaysAndHours", settings.onDaysAndHours)
        put("days", JsonArray(settings.days.sorted().map { JsonPrimitive(it) }))
        put("startMinute", settings.startMinute)
        put("endMinute", settings.endMinute)
    }.toString()

    private fun decode(text: String): HeadphoneSettings? = runCatching {
        val json = Json.parseToJsonElement(text).jsonObject
        HeadphoneSettings(
            wired = json["wired"]!!.jsonPrimitive.boolean,
            bluetooth = json["bluetooth"]!!.jsonPrimitive.boolean,
            devices = json["devices"]!!.jsonArray.map { element ->
                val device = element.jsonObject
                ChosenHeadphones(device["address"]!!.jsonPrimitive.content, device["name"]!!.jsonPrimitive.content)
            },
            onlyAtTimes = json["onlyAtTimes"]!!.jsonPrimitive.boolean,
            duringPlanWindow = json["duringPlanWindow"]!!.jsonPrimitive.boolean,
            planCategoryIds = json["planCategoryIds"]?.jsonArray?.map { UUID.fromString(it.jsonPrimitive.content) }?.toSet(),
            onDaysAndHours = json["onDaysAndHours"]!!.jsonPrimitive.boolean,
            days = json["days"]!!.jsonArray.map { it.jsonPrimitive.int }.toSet(),
            startMinute = json["startMinute"]!!.jsonPrimitive.int,
            endMinute = json["endMinute"]!!.jsonPrimitive.int,
        )
    }.getOrNull()

    companion object {
        const val PREFS = "modeSettings"
        const val KEY_ACTIVE = "activeMode"
        const val KEY_SWITCHING = "switching"
    }
}
