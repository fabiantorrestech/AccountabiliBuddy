package com.accountabilibuddy.companion.data

import com.accountabilibuddy.companion.data.JsonValues.bool
import com.accountabilibuddy.companion.data.JsonValues.string
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.SleepSettings
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.put
import java.io.File
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter

/**
 * Keeps the phone's data as small JSON files in the app's private folder (never backed up or sent anywhere):
 * the PC's documents exactly as imported (profiles, sleep, day bar, events) and the phone's own state.
 * Thread-safe: writes are serialised and [state] always holds the latest.
 */
class CompanionStore(private val folder: File) {
    private val lock = Any()
    private val _state = MutableStateFlow(load())

    val state: StateFlow<CompanionState> = _state.asStateFlow()

    /** Replaces the profiles (and sleep, day bar and events when the import has them). */
    fun import(data: ImportedData) = synchronized(lock) {
        write(PROFILES, data.profilesJson)
        data.sleepJson?.let { write(SLEEP, it) }
        data.dayBarJson?.let { write(DAY_BAR, it) }
        data.eventsJson?.let { write(EVENTS, it) }
        val now = LocalDateTime.now().format(STAMP)
        _state.update {
            it.copy(
                document = data.document,
                sleep = data.sleep ?: it.sleep,
                dayBar = data.dayBar ?: it.dayBar,
                events = if (data.eventsJson != null) data.events else it.events,
                importedFrom = data.sourceDescription,
                importedAt = now,
            )
        }
        savePhone()
    }

    fun saveSleep(settings: SleepSettings) = synchronized(lock) {
        write(SLEEP, SettingsJson.sleepJson(settings))
        _state.update { it.copy(sleep = settings) }
    }

    fun setTimelineNotification(on: Boolean) = updatePhone { it.copy(timelineNotificationOn = on) }

    fun setBedtimeReminder(on: Boolean) = updatePhone { it.copy(bedtimeReminderOn = on) }

    fun setLastReminderNight(night: String) = updatePhone { it.copy(lastReminderNight = night) }

    fun setSlept(windows: List<LocalWindow>) = updatePhone { it.copy(slept = windows) }

    /** Forgets everything imported (the phone's own choices stay). */
    fun clearImport() = synchronized(lock) {
        listOf(PROFILES, EVENTS, DAY_BAR).forEach { File(folder, it).delete() }
        _state.update { CompanionState(sleep = it.sleep, timelineNotificationOn = it.timelineNotificationOn, bedtimeReminderOn = it.bedtimeReminderOn) }
        savePhone()
    }

    private fun updatePhone(change: (CompanionState) -> CompanionState) = synchronized(lock) {
        _state.update(change)
        savePhone()
    }

    private fun savePhone() {
        val state = _state.value
        write(PHONE, buildJsonObject {
            state.importedFrom?.let { put("importedFrom", it) }
            state.importedAt?.let { put("importedAt", it) }
            put("timelineNotificationOn", state.timelineNotificationOn)
            put("bedtimeReminderOn", state.bedtimeReminderOn)
            state.lastReminderNight?.let { put("lastReminderNight", it) }
            put("slept", buildJsonArray {
                state.slept.forEach { window ->
                    add(buildJsonObject {
                        put("start", window.start.toString())
                        put("end", window.end.toString())
                    })
                }
            })
        })
    }

    private fun load(): CompanionState {
        var state = CompanionState()
        read(PROFILES)?.let { json ->
            runCatching { ProfileDocumentParser.parse(json.jsonObject) }.onSuccess { state = state.copy(document = it) }
        }
        read(SLEEP)?.let { json -> runCatching { SettingsJson.sleep(json.jsonObject) }.onSuccess { state = state.copy(sleep = it) } }
        read(DAY_BAR)?.let { json -> runCatching { SettingsJson.dayBar(json.jsonObject) }.onSuccess { state = state.copy(dayBar = it) } }
        read(EVENTS)?.let { json -> (json as? JsonArray)?.let { state = state.copy(events = SettingsJson.events(it)) } }
        (read(PHONE) as? JsonObject)?.let { phone ->
            state = state.copy(
                importedFrom = phone.string("importedFrom"),
                importedAt = phone.string("importedAt"),
                timelineNotificationOn = phone.bool("timelineNotificationOn", true),
                bedtimeReminderOn = phone.bool("bedtimeReminderOn", true),
                lastReminderNight = phone.string("lastReminderNight"),
                slept = (phone["slept"] as? JsonArray).orEmpty().mapNotNull { element ->
                    val o = element as? JsonObject ?: return@mapNotNull null
                    runCatching { LocalWindow(LocalDateTime.parse(o.string("start")), LocalDateTime.parse(o.string("end"))) }.getOrNull()
                },
            )
        }

        return state
    }

    private fun read(name: String): JsonElement? {
        val file = File(folder, name)
        return if (file.exists()) runCatching { Json.parseToJsonElement(file.readText()) }.getOrNull() else null
    }

    private fun write(name: String, json: JsonElement) {
        folder.mkdirs()
        val file = File(folder, name)
        val temporary = File(folder, "$name.tmp")
        temporary.writeText(json.toString())
        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }
    }

    companion object {
        private const val PROFILES = "profiles.json"
        private const val SLEEP = "sleep.json"
        private const val DAY_BAR = "daybar.json"
        private const val EVENTS = "events.json"
        private const val PHONE = "phone.json"
        private val STAMP: DateTimeFormatter = DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm")
    }
}
