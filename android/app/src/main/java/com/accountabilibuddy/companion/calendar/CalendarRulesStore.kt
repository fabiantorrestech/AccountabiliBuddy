package com.accountabilibuddy.companion.calendar

import android.content.Context
import com.accountabilibuddy.companion.model.CalendarRule
import com.accountabilibuddy.companion.model.CalendarRuleMode
import com.accountabilibuddy.companion.model.CalendarRuleSet
import com.accountabilibuddy.companion.model.CalendarRules
import com.accountabilibuddy.companion.model.KeywordCategory
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.put
import java.time.Duration
import java.time.Instant
import java.util.UUID

/**
 * The phone's own calendar rules (ADR 58): which of its calendars give events a day plan category, kept in preferences "calendarRules"
 * and never synced, backed up or sent to the watch (the phone's backup lists the preference files it takes, and this is not one). Saving
 * a change always waits [DELAY] (changing a rule is a looser change, by the phone's clock like its other waits) and is refused while a
 * focus session runs; a waiting change is promoted when due and no session runs (a wait that ends in a session applies after it). There
 * is no "apply now": nothing here shortens the wait, and a newer save restarts it.
 */
class CalendarRulesStore(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
    private val _rules = MutableStateFlow(load())

    /** The rules in force and any change waiting; call [settle] first for a view that has promoted a due change. */
    val rules: StateFlow<CalendarRules> = _rules.asStateFlow()

    /** The rules in force now: a waiting set whose time has come takes over, unless a session runs. */
    @Synchronized
    fun settle(now: Instant, sessionRunning: Boolean): CalendarRules {
        val current = _rules.value
        if (!sessionRunning && current.pendingIsDue(now)) {
            val promoted = current.promoted()
            save(promoted)
            return promoted
        }

        return current
    }

    /**
     * Schedules [set] to take over after [DELAY], replacing a set already waiting (the wait starts over). False, and nothing is saved,
     * while a session runs. A set that equals the one in force and none waits is not a change: it only clears a waiting set.
     */
    @Synchronized
    fun trySave(set: CalendarRuleSet, now: Instant, sessionRunning: Boolean): Boolean {
        if (sessionRunning) return false

        val current = settle(now, sessionRunning)
        if (set == current.active) {
            if (current.pending != null) save(CalendarRules(current.active))
            return true
        }

        save(current.withPending(set, now.plus(DELAY)))
        return true
    }

    /** Drops the waiting change; the rules in force stay. Allowed at any time (it only keeps today's rules). */
    @Synchronized
    fun cancelPending() {
        val current = _rules.value
        if (current.pending != null) save(CalendarRules(current.active))
    }

    private fun save(rules: CalendarRules) {
        _rules.value = rules
        prefs.edit().putString(KEY, toJson(rules).toString()).apply()
    }

    private fun load(): CalendarRules {
        val text = prefs.getString(KEY, null) ?: return CalendarRules()
        return runCatching { fromJson(Json.parseToJsonElement(text) as JsonObject) }.getOrDefault(CalendarRules())
    }

    companion object {
        /** How long a saved change waits: the phone's looser-change wait (10 minutes, as its other waits; the PC's setting does not reach the phone). */
        val DELAY: Duration = Duration.ofMinutes(10)
        private const val PREFS = "calendarRules"
        private const val KEY = "json"

        internal fun toJson(rules: CalendarRules): JsonObject = buildJsonObject {
            put("active", setJson(rules.active))
            rules.pending?.let { put("pending", setJson(it)) }
            rules.pendingTakesOverAt?.let { put("takesOverAt", it.toString()) }
        }

        internal fun fromJson(json: JsonObject): CalendarRules {
            val pending = (json["pending"] as? JsonObject)?.let(::setFrom)
            return CalendarRules(
                active = (json["active"] as? JsonObject)?.let(::setFrom) ?: CalendarRuleSet.Empty,
                pending = pending,
                pendingTakesOverAt = if (pending == null) null else (json["takesOverAt"] as? JsonPrimitive)?.contentOrNull?.let { Instant.parse(it) },
            )
        }

        private fun setJson(set: CalendarRuleSet): JsonObject = buildJsonObject {
            put("enabled", set.enabled)
            put("rules", buildJsonArray {
                set.rules.forEach { rule ->
                    add(buildJsonObject {
                        put("calendarId", rule.calendarId)
                        put("mode", rule.mode.name)
                        rule.categoryId?.let { put("categoryId", it.toString()) }
                        rule.ownKeywords?.let { put("ownKeywords", keywordsJson(it)) }
                    })
                }
            })
            put("globalKeywords", keywordsJson(set.globalKeywords))
        }

        private fun keywordsJson(keywords: List<KeywordCategory>): JsonArray = buildJsonArray {
            keywords.forEach { add(buildJsonObject { put("keyword", it.keyword); put("categoryId", it.categoryId.toString()) }) }
        }

        private fun setFrom(json: JsonObject): CalendarRuleSet = CalendarRuleSet(
            enabled = (json["enabled"] as? JsonPrimitive)?.booleanOrNull ?: false,
            rules = (json["rules"] as? JsonArray).orEmpty().mapNotNull { element ->
                val o = element as? JsonObject ?: return@mapNotNull null
                val calendarId = (o["calendarId"] as? JsonPrimitive)?.contentOrNull ?: return@mapNotNull null
                val mode = CalendarRuleMode.entries.firstOrNull { it.name == (o["mode"] as? JsonPrimitive)?.contentOrNull } ?: return@mapNotNull null
                CalendarRule(calendarId, mode, uuid(o["categoryId"]), (o["ownKeywords"] as? JsonArray)?.let(::keywordsFrom))
            },
            globalKeywords = (json["globalKeywords"] as? JsonArray)?.let(::keywordsFrom).orEmpty(),
        )

        private fun keywordsFrom(array: JsonArray): List<KeywordCategory> = array.mapNotNull { element ->
            val o = element as? JsonObject ?: return@mapNotNull null
            val keyword = (o["keyword"] as? JsonPrimitive)?.contentOrNull ?: return@mapNotNull null
            val id = uuid(o["categoryId"]) ?: return@mapNotNull null
            KeywordCategory(keyword, id)
        }

        private fun uuid(element: kotlinx.serialization.json.JsonElement?): UUID? =
            (element as? JsonPrimitive)?.takeIf { it !is JsonNull }?.contentOrNull?.let { runCatching { UUID.fromString(it) }.getOrNull() }
    }
}
