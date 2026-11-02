package com.accountabilibuddy.companion.pause

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId

/**
 * The phone's master switch and car pause ([PauseState]), kept in preferences "pause" so a pause, a waiting request
 * and the car's state survive restarts. [advance] applies what's due (call it on each refresh).
 */
class PauseStore(context: Context) {
    private val prefs = context.getSharedPreferences("pause", Context.MODE_PRIVATE)
    private val _state = MutableStateFlow(load())

    val state: StateFlow<PauseState> = _state.asStateFlow()

    val isQuiet: Boolean get() = PauseRules.isQuiet(advance())

    /** Whether a focus session runs at an instant (set by the app); a request whose wait is over is not applied while one does. */
    @Volatile
    var sessionRunning: (Instant) -> Boolean = { false }

    /**
     * Applies a request whose wait is over and ends a pause whose time is up. A request that falls due during a focus session waits
     * for it to end (the guard settings rest then) and takes effect from the last moment the session was seen holding it.
     */
    @Synchronized
    fun advance(now: Instant = Instant.now()): PauseState {
        val running = sessionRunning(now)
        val state = _state.value
        if (running && PauseRules.due(state, now)) {
            prefs.edit().putString(KEY_HELD_AT, now.toString()).apply()
        }

        val heldAt = prefs.getString(KEY_HELD_AT, null)?.let { runCatching { Instant.parse(it) }.getOrNull() }
        val next = PauseRules.advance(state, now, ZoneId.systemDefault(), sessionRunning = running, heldUntil = heldAt)
        if ((heldAt != null && state.requestedAt == null) || (next.pausedAt != null && state.pausedAt == null)) {
            prefs.edit().remove(KEY_HELD_AT).apply()
        }

        if (next != _state.value) save(next)
        return next
    }

    @Synchronized
    fun requestOff(length: PauseLength, waitMinutes: Int, untilTime: LocalTime) {
        save(_state.value.copy(requestedAt = Instant.now(), waitMinutes = waitMinutes, length = length, untilTime = untilTime))
    }

    /** Cancels a waiting request, or turns guiding back on: at once. */
    @Synchronized
    fun turnOn() {
        save(PauseRules.resume(_state.value, Instant.now()))
    }

    @Synchronized
    fun setCarPause(on: Boolean) {
        val now = Instant.now()
        val inCar = _state.value.inCar
        // Re-run the car rule so a quiet stretch opens or closes as the switch flips.
        val off = PauseRules.car(_state.value, inCar = false, now)
        save(PauseRules.car(off.copy(carPause = on), inCar = inCar, now))
    }

    @Synchronized
    fun setInCar(inCar: Boolean) {
        save(PauseRules.car(_state.value, inCar, Instant.now()))
    }

    @Synchronized
    fun reminded(day: LocalDate) {
        save(_state.value.copy(lastReminder = day))
    }

    private fun save(state: PauseState) {
        _state.value = state
        val json = buildJsonObject {
            state.requestedAt?.let { put("requestedAt", it.toString()) }
            put("waitMinutes", state.waitMinutes)
            put("length", state.length.name)
            put("untilTime", state.untilTime.toString())
            state.pausedAt?.let { put("pausedAt", it.toString()) }
            put("carPause", state.carPause)
            put("inCar", state.inCar)
            state.lastReminder?.let { put("lastReminder", it.toString()) }
            put("quiet", JsonArray(state.quiet.map { stretch ->
                buildJsonObject {
                    put("start", stretch.start.toString())
                    stretch.end?.let { put("end", it.toString()) }
                }
            }))
        }
        prefs.edit().putString("state", json.toString()).apply()
    }

    private companion object {
        /** When a focus session was last seen holding a due request (kept apart from the state: the request is unchanged). */
        const val KEY_HELD_AT = "heldAt"
    }

    private fun load(): PauseState {
        val json = runCatching { Json.parseToJsonElement(prefs.getString("state", null) ?: return PauseState()).jsonObject }.getOrNull()
            ?: return PauseState()
        fun text(name: String): String? = json[name]?.jsonPrimitive?.content
        return runCatching {
            PauseState(
                requestedAt = text("requestedAt")?.let(Instant::parse),
                waitMinutes = text("waitMinutes")?.toIntOrNull() ?: 10,
                length = text("length")?.let { runCatching { PauseLength.valueOf(it) }.getOrNull() } ?: PauseLength.RestOfDay,
                untilTime = text("untilTime")?.let(LocalTime::parse) ?: LocalTime.of(2, 0),
                pausedAt = text("pausedAt")?.let(Instant::parse),
                carPause = text("carPause")?.toBooleanStrictOrNull() ?: false,
                inCar = text("inCar")?.toBooleanStrictOrNull() ?: false,
                lastReminder = text("lastReminder")?.let(LocalDate::parse),
                quiet = json["quiet"]?.jsonArray?.mapNotNull { element ->
                    val item = element as? JsonObject ?: return@mapNotNull null
                    val start = item["start"]?.jsonPrimitive?.content?.let(Instant::parse) ?: return@mapNotNull null
                    QuietStretch(start, item["end"]?.jsonPrimitive?.content?.let(Instant::parse))
                }.orEmpty(),
            )
        }.getOrDefault(PauseState())
    }
}
