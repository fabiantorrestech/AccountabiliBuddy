package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.shared.TokenVault
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.time.Duration
import java.time.Instant

/**
 * The reasons the user gave for a 4th task on the next session's list ("Add anyway", ADR 48), as the PC keeps them for
 * its own list: at least [MIN_REASON_LENGTH] characters, sealed with a key in the Android Keystore ([TokenVault]), kept
 * 90 days, and never sent to another device or logged.
 */
class OverLimitStore(context: Context, private val now: () -> Instant = { Instant.now() }) {
    private val prefs = context.getSharedPreferences("over_limit", Context.MODE_PRIVATE)

    /**
     * Keeps [reason] for an "Add anyway".
     *
     * @return False when the reason is shorter than [MIN_REASON_LENGTH] characters (nothing is kept).
     */
    @Synchronized
    fun record(reason: String): Boolean {
        val trimmed = reason.trim()
        if (trimmed.length < MIN_REASON_LENGTH) {
            return false
        }

        val time = now()
        val entries = read().filter { it.first.isAfter(time.minus(KEPT)) } + (time to trimmed)
        val json = JsonArray(entries.map { (at, text) -> buildJsonObject { put("at", at.toString()); put("reason", text) } })
        prefs.edit().putString(KEY, TokenVault.seal(json.toString(), ALIAS)).apply()
        return true
    }

    // Empty when nothing is stored or it can't be opened (the key goes with the app's data).
    private fun read(): List<Pair<Instant, String>> = runCatching {
        val text = prefs.getString(KEY, null)?.let { TokenVault.open(it, ALIAS) } ?: return emptyList()
        (Json.parseToJsonElement(text) as JsonArray).mapNotNull { element ->
            val entry = element as? JsonObject ?: return@mapNotNull null
            runCatching { Instant.parse(entry.getValue("at").jsonPrimitive.content) to entry.getValue("reason").jsonPrimitive.content }.getOrNull()
        }
    }.getOrDefault(emptyList())

    companion object {
        /** As on the PC (SessionTasksService.MinOverLimitReasonLength). */
        const val MIN_REASON_LENGTH = 10

        /** The tasks a list holds before the "Keep it to 3" prompt (the PC's MaxTasks). */
        const val MAX_TASKS = 3

        private const val KEY = "reasons"
        private const val ALIAS = "accountabilibuddy-over-limit"
        private val KEPT: Duration = Duration.ofDays(90)
    }
}
