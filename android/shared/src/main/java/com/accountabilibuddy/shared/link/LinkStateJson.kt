package com.accountabilibuddy.shared.link

import com.accountabilibuddy.shared.RunningSession
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import java.time.OffsetDateTime

/** The PC's "state" message (its LinkState) as the watch reads it when it talks to the PC itself (ADR 21). */
object LinkStateJson {
    /**
     * The session running on the PC when it's one that counts as focus (a scheduled block isn't); breaks belong to it.
     * The same rule as the phone's PhoneSnapshot.running.
     */
    fun running(state: JsonObject): RunningSession? {
        val mode = state.string("mode") ?: return null
        if (mode == "scheduled") {
            return null
        }

        val phase = state.string("phase") ?: "idle"
        val startedAt = state.time("phaseStartedAt") ?: return null
        return RunningSession(state.string("profileName"), startedAt, state.time("phaseEndsAt"), onBreak = phase == "shortBreak" || phase == "longBreak")
            .withTasks(com.accountabilibuddy.shared.sync.SessionTaskItem.read(state["tasks"] as? kotlinx.serialization.json.JsonArray))
    }

    private fun JsonObject.string(name: String): String? = (this[name] as? JsonPrimitive)?.takeIf { it !is JsonNull && it.isString }?.content

    private fun JsonObject.time(name: String) = string(name)?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() }
}
