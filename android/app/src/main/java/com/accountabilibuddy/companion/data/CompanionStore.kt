package com.accountabilibuddy.companion.data

import com.accountabilibuddy.companion.data.JsonValues.bool
import com.accountabilibuddy.companion.data.JsonValues.string
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.PendingDayPlan
import com.accountabilibuddy.companion.model.PendingStatus
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
import java.time.Instant
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
        data.eventsJson?.let {
            write(EVENTS, it)
            write(EVENTS_AT, buildJsonObject { put("at", Instant.now().toString()) })
        }
        if (data.dayPlanVersion != null) {
            write(PLAN_META, buildJsonObject {
                put("version", data.dayPlanVersion)
                data.dayPlanEditedAt?.let { put("editedAt", it.toString()) }
            })
        }

        val now = LocalDateTime.now().format(STAMP)
        _state.update {
            // A day plan edit still waiting for the PC keeps showing over the PC's plan (ADR 19).
            val shown = it.pendingPlan?.let { pending -> data.document.copy(dayPlan = pending.edited) } ?: data.document
            // A PC that was too old for edits sends its plan version once updated: the waiting edit goes again.
            val pending = it.pendingPlan?.let { waiting ->
                val version = data.dayPlanVersion
                // The PC's plan is the one the edit started from but under a new version (the v12 upgrade changed the
                // hashing, not the plan): the edit builds on that version, so it isn't taken for a clash.
                val rebased = if (version != null && waiting.baseVersion != version && waiting.basePlan == data.document.dayPlan) {
                    waiting.copy(baseVersion = version)
                } else {
                    waiting
                }
                if (rebased.status == PendingStatus.NeedsPcUpdate && version != null) {
                    rebased.copy(status = PendingStatus.Waiting, message = null).also(::writePending)
                } else {
                    rebased.also { next -> if (next !== waiting) writePending(next) }
                }
            }
            it.copy(
                pendingPlan = pending,
                document = shown,
                pcDayPlan = data.document.dayPlan,
                pcPlanVersion = data.dayPlanVersion,
                pcPlanEditedAt = data.dayPlanEditedAt,
                sleep = data.sleep?.copy(alarm = it.sleep.alarm, wakeEstimates = it.sleep.wakeEstimates) ?: it.sleep,
                dayBar = data.dayBar ?: it.dayBar,
                events = if (data.eventsJson != null) data.events else it.events,
                importedFrom = data.sourceDescription,
                importedAt = now,
            )
        }
        savePhone()
    }

    /**
     * A PC's calendar events from its sync file (ADR 26), taken when written after the events the phone has (from an
     * older file), so the Calendar row stays current with Drive.
     */
    fun eventsFromFile(json: JsonArray, writtenAt: Instant) = synchronized(lock) {
        val have = (read(EVENTS_AT) as? JsonObject)?.string("at")?.let { runCatching { Instant.parse(it) }.getOrNull() }
        if (have != null && !writtenAt.isAfter(have)) {
            return@synchronized
        }

        write(EVENTS, json)
        write(EVENTS_AT, buildJsonObject { put("at", writtenAt.toString()) })
        _state.update { it.copy(events = SettingsJson.events(json)) }
    }

    /** This phone's next alarm as its wake time (ADR 23), or null; kept with the sleep settings in memory only. */
    fun setPhoneAlarm(alarm: java.time.LocalDateTime?) = synchronized(lock) {
        _state.update { it.copy(sleep = it.sleep.copy(alarm = alarm)) }
    }

    /** The smart bedtime's levels now in use (ADR 34); kept in memory only, like the alarm. */
    fun setSmartLevels(levels: Map<com.accountabilibuddy.companion.engine.NightKind, Int>) = synchronized(lock) {
        _state.update { if (it.sleep.smartLevels == levels) it else it.copy(sleep = it.sleep.copy(smartLevels = levels)) }
    }

    /** The estimated wake times per kind of morning (ADR 40); kept in memory only, like the alarm. */
    fun setWakeEstimates(estimates: Map<com.accountabilibuddy.companion.engine.NightKind, java.time.LocalTime>) = synchronized(lock) {
        _state.update { if (it.sleep.wakeEstimates == estimates) it else it.copy(sleep = it.sleep.copy(wakeEstimates = estimates)) }
    }

    fun saveSleep(settings: SleepSettings) = synchronized(lock) {
        write(SLEEP, SettingsJson.sleepJson(settings))
        onSettingsSaved()
        _state.update { it.copy(sleep = settings.copy(alarm = it.sleep.alarm, smartLevels = it.sleep.smartLevels, wakeEstimates = it.sleep.wakeEstimates)) }
    }

    fun setTimelineNotification(on: Boolean) = updatePhone { it.copy(timelineNotificationOn = on) }

    fun setBedtimeReminder(on: Boolean) = updatePhone { it.copy(bedtimeReminderOn = on) }

    fun setLastReminderNight(night: String) = updatePhone { it.copy(lastReminderNight = night) }

    fun setSlept(windows: List<LocalWindow>) = updatePhone { it.copy(slept = windows) }

    /**
     * Keeps a day plan edited on the phone until the PC takes it (ADR 19). [baseVersion] and [basePlan] are the PC
     * plan the editor opened on; a second edit before the PC takes the first keeps the first one's starting point.
     */
    fun savePendingPlan(edited: DayPlan, editedAt: Instant, baseVersion: String, basePlan: DayPlan) = synchronized(lock) {
        val current = _state.value
        val pending = current.pendingPlan?.copy(edited = edited, editedAt = editedAt, status = PendingStatus.Waiting, message = null)
            ?: PendingDayPlan(baseVersion, basePlan, edited, editedAt)
        writePending(pending)
        _state.update { it.copy(pendingPlan = pending, document = it.document.copy(dayPlan = edited)) }
    }

    /** Sets the waiting edit's status, unless a newer edit replaced the one that was sent ([sentEditedAt]). */
    fun setPendingStatus(sentEditedAt: Instant, status: PendingStatus, message: String?) = synchronized(lock) {
        val current = _state.value.pendingPlan?.takeIf { it.editedAt == sentEditedAt } ?: return@synchronized
        val pending = current.copy(status = status, message = message)
        writePending(pending)
        _state.update { it.copy(pendingPlan = pending) }
    }

    /**
     * The PC took the edit made at [sentEditedAt] ([accepted]). Done, unless a newer edit was saved meanwhile: that one
     * now builds on what the PC took ([pcVersion]), so it doesn't come back as a conflict with the user's own change.
     */
    fun finishPendingPlan(sentEditedAt: Instant, accepted: DayPlan, pcVersion: String?) = synchronized(lock) {
        val current = _state.value.pendingPlan ?: return@synchronized
        if (current.editedAt == sentEditedAt) {
            File(folder, PENDING_PLAN).delete()
            _state.update { it.copy(pendingPlan = null, pcDayPlan = accepted, document = it.document.copy(dayPlan = accepted)) }
            return@synchronized
        }

        // A newer edit was saved while this one was on its way: it now builds on what the PC took.
        val rebased = if (pcVersion == null) current else current.copy(baseVersion = pcVersion, basePlan = accepted, status = PendingStatus.Waiting, message = null)
        writePending(rebased)
        _state.update { it.copy(pendingPlan = rebased, pcDayPlan = accepted, pcPlanVersion = pcVersion ?: it.pcPlanVersion) }
    }

    /** The PC's current plan as a conflict answer brought it (newer than the last settings push). */
    fun updatePcPlan(plan: DayPlan, version: String, editedAt: Instant?) = synchronized(lock) {
        _state.update { it.copy(pcDayPlan = plan, pcPlanVersion = version, pcPlanEditedAt = editedAt ?: it.pcPlanEditedAt) }
    }

    /** "Use this phone's" after a conflict: the edit now builds on the PC's current plan and goes again. */
    fun rebasePending(pcPlan: DayPlan, pcVersion: String) = synchronized(lock) {
        val pending = _state.value.pendingPlan?.copy(baseVersion = pcVersion, basePlan = pcPlan, status = PendingStatus.Waiting, message = null)
            ?: return@synchronized
        writePending(pending)
        _state.update { it.copy(pendingPlan = pending, pcDayPlan = pcPlan, pcPlanVersion = pcVersion) }
    }

    /** The edit is done with: [accepted] (the PC took it, or the user kept the PC's) is shown, else the PC's plan. */
    fun clearPendingPlan(accepted: DayPlan?) = synchronized(lock) {
        File(folder, PENDING_PLAN).delete()
        _state.update {
            val plan = accepted ?: it.pcDayPlan
            it.copy(pendingPlan = null, pcDayPlan = plan, document = it.document.copy(dayPlan = plan))
        }
    }

    private fun writePending(pending: PendingDayPlan) {
        write(PENDING_PLAN, buildJsonObject {
            put("baseVersion", pending.baseVersion)
            put("basePlan", DayPlanJson.write(pending.basePlan))
            put("edited", DayPlanJson.write(pending.edited))
            put("editedAt", pending.editedAt.toString())
            put("status", pending.status.name)
            pending.message?.let { put("message", it) }
        })
    }

    private fun readPending(json: JsonObject): PendingDayPlan? = runCatching {
        PendingDayPlan(
            baseVersion = json.string("baseVersion").orEmpty(),
            basePlan = ProfileDocumentParser.dayPlan(json["basePlan"]!!.jsonObject),
            edited = ProfileDocumentParser.dayPlan(json["edited"]!!.jsonObject),
            editedAt = Instant.parse(json.string("editedAt")),
            status = PendingStatus.entries.firstOrNull { it.name == json.string("status") } ?: PendingStatus.Waiting,
            message = json.string("message"),
        )
    }.getOrNull()

    /** Forgets everything imported (the phone's own choices stay). */
    fun clearImport() = synchronized(lock) {
        listOf(PROFILES, EVENTS, DAY_BAR, PLAN_META, PENDING_PLAN).forEach { File(folder, it).delete() }
        _state.update { CompanionState(sleep = it.sleep, timelineNotificationOn = it.timelineNotificationOn, bedtimeReminderOn = it.bedtimeReminderOn) }
        savePhone()
    }

    private fun updatePhone(change: (CompanionState) -> CompanionState) = synchronized(lock) {
        _state.update(change)
        savePhone()
    }

    /** Called after this phone's own settings are saved (for the auto-backup). */
    var onSettingsSaved: () -> Unit = {}

    private fun savePhone() {
        onSettingsSaved()
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
            runCatching { ProfileDocumentParser.parse(json.jsonObject) }.onSuccess { state = state.copy(document = it, pcDayPlan = it.dayPlan) }
        }
        (read(PLAN_META) as? JsonObject)?.let { meta ->
            state = state.copy(
                pcPlanVersion = meta.string("version"),
                pcPlanEditedAt = meta.string("editedAt")?.let { runCatching { Instant.parse(it) }.getOrNull() },
            )
        }
        (read(PENDING_PLAN) as? JsonObject)?.let(::readPending)?.let { pending ->
            state = state.copy(pendingPlan = pending, document = state.document.copy(dayPlan = pending.edited))
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
        private const val EVENTS_AT = "events-at.json"
        private const val PHONE = "phone.json"
        private const val PLAN_META = "dayplan-meta.json"
        private const val PENDING_PLAN = "dayplan-pending.json"
        private val STAMP: DateTimeFormatter = DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm")
    }
}
