package com.accountabilibuddy.companion.calendar

import android.content.Context
import com.accountabilibuddy.companion.model.ClassificationScope
import com.accountabilibuddy.companion.model.EventClassification
import com.accountabilibuddy.companion.model.PendingClassification
import com.accountabilibuddy.shared.TokenVault
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.put

/**
 * The categories chosen by hand on this phone's own calendar events (ADR 64). The keys say which of the user's events they are about, so they are sealed
 * with a key in the Android Keystore ([TokenVault]), never logged, never synced or backed up. As on the PC: a change to the same or a stricter choice applies
 * at once; a looser one waits [CalendarRulesStore.DELAY] with the old choice standing, and nothing changes while a focus session runs. There is no "apply now".
 */
class EventClassificationStore(context: Context) {
    private val prefs = context.getSharedPreferences("event_classifications", Context.MODE_PRIVATE)
    private val _classifications = MutableStateFlow<List<EventClassification>>(emptyList())
    private val _pending = MutableStateFlow<List<PendingClassification>>(emptyList())

    /** The choices in force; call [settle] first for a view that has promoted what is due. */
    val classifications: StateFlow<List<EventClassification>> = _classifications.asStateFlow()

    val pending: StateFlow<List<PendingClassification>> = _pending.asStateFlow()

    init {
        read()
    }

    /** The choices in force now: waiting changes whose time has come take over, unless a session runs. */
    @Synchronized
    fun settle(now: Instant, sessionRunning: Boolean): List<EventClassification> {
        val due = _pending.value.filter { !it.takesOverAt.isAfter(now) }
        if (!sessionRunning && due.isNotEmpty()) {
            var list = _classifications.value
            for (change in due.sortedBy { it.takesOverAt }) {
                list = list.filterNot { same(it, change.change) } + listOfNotNull(change.change.takeUnless { change.removes })
            }

            _classifications.value = list
            _pending.value = _pending.value - due.toSet()
            write()
        }

        return _classifications.value
    }

    /**
     * Stores or removes ([removes]) a choice: at once, or after the delay when [looser]. False, and nothing is saved, while a session runs.
     */
    @Synchronized
    fun trySet(target: EventClassification, removes: Boolean, looser: Boolean, now: Instant, sessionRunning: Boolean): Boolean {
        if (sessionRunning) return false

        _pending.value = _pending.value.filterNot { same(it.change, target) }
        if (looser) {
            _pending.value = _pending.value + PendingClassification(target, removes, now.plus(CalendarRulesStore.DELAY))
        } else {
            _classifications.value = _classifications.value.filterNot { same(it, target) } + listOfNotNull(target.takeUnless { removes })
        }

        write()
        return true
    }

    private fun same(a: EventClassification, b: EventClassification) = a.calendarId == b.calendarId && a.scope == b.scope && a.key == b.key

    private fun write() {
        val json = buildJsonObject {
            put("active", buildJsonArray { _classifications.value.forEach { add(toJson(it)) } })
            put(
                "pending",
                buildJsonArray {
                    _pending.value.forEach { waiting ->
                        add(
                            buildJsonObject {
                                put("change", toJson(waiting.change))
                                put("removes", waiting.removes)
                                put("at", waiting.takesOverAt.toString())
                            },
                        )
                    }
                },
            )
        }
        prefs.edit().putString(KEY, TokenVault.seal(json.toString(), ALIAS)).apply()
    }

    private fun toJson(item: EventClassification): JsonObject = buildJsonObject {
        put("calendarId", item.calendarId)
        put("key", item.key)
        put("scope", item.scope.name)
        item.categoryId?.let { put("categoryId", it.toString()) }
    }

    // Empty when nothing is stored or it can't be opened (the key goes with the app's data): nothing classified, the plan as stored.
    private fun read() {
        val text = prefs.getString(KEY, null)?.let { TokenVault.open(it, ALIAS) } ?: return
        val json = runCatching { Json.parseToJsonElement(text) as? JsonObject }.getOrNull() ?: return
        _classifications.value = (json["active"] as? JsonArray).orEmpty().mapNotNull { fromJson(it as? JsonObject) }
        _pending.value = (json["pending"] as? JsonArray).orEmpty().mapNotNull { element ->
            val item = element as? JsonObject ?: return@mapNotNull null
            val change = fromJson(item["change"] as? JsonObject) ?: return@mapNotNull null
            val at = (item["at"] as? JsonPrimitive)?.contentOrNull?.let { runCatching { Instant.parse(it) }.getOrNull() } ?: return@mapNotNull null
            PendingClassification(change, (item["removes"] as? JsonPrimitive)?.contentOrNull == "true", at)
        }
    }

    private fun fromJson(item: JsonObject?): EventClassification? {
        fun text(name: String) = (item?.get(name) as? JsonPrimitive)?.contentOrNull
        val calendarId = text("calendarId") ?: return null
        val key = text("key") ?: return null
        val scope = runCatching { ClassificationScope.valueOf(text("scope") ?: "") }.getOrNull() ?: return null
        return EventClassification(calendarId, key, scope, text("categoryId")?.let { runCatching { UUID.fromString(it) }.getOrNull() })
    }

    private companion object {
        const val KEY = "list"
        const val ALIAS = "accountabilibuddy-event-classifications"
    }
}
