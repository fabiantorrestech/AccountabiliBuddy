package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.shared.TokenVault
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

/** A saved filter on the Tasks tab: the category chip and the search words, under a name. [category] null is All. */
data class TaskFilterPreset(val id: String, val name: String, val category: String?, val search: String)

/**
 * The Tasks tab's saved filters. The search words and names are the user's text: in the app's private prefs, sealed with a key in the
 * Android Keystore ([TokenVault]), never logged, never synced or in a backup. Saving under a name that exists (any case) replaces that one.
 */
class TaskFilterPresetsStore(context: Context) {
    private val prefs = context.getSharedPreferences("task_filter_presets", Context.MODE_PRIVATE)
    private val _presets = MutableStateFlow(read())

    val presets: StateFlow<List<TaskFilterPreset>> = _presets.asStateFlow()

    @Synchronized
    fun save(name: String, category: String?, search: String) {
        val trimmed = name.trim().take(MAX_NAME_LENGTH)
        if (trimmed.isEmpty()) return
        val list = _presets.value
        val held = list.firstOrNull { it.name.equals(trimmed, ignoreCase = true) }
        if (held == null && list.size >= MAX_PRESETS) return
        val saved = TaskFilterPreset(held?.id ?: UUID.randomUUID().toString(), trimmed, category, search.trim())
        write(if (held != null) list.map { if (it.id == held.id) saved else it } else list + saved)
    }

    @Synchronized
    fun delete(id: String) {
        write(_presets.value.filterNot { it.id == id })
    }

    private fun write(list: List<TaskFilterPreset>) {
        val json = buildJsonArray {
            list.forEach { preset ->
                add(
                    buildJsonObject {
                        put("id", preset.id)
                        put("name", preset.name)
                        preset.category?.let { put("category", it) }
                        put("search", preset.search)
                    },
                )
            }
        }
        prefs.edit().putString(KEY, TokenVault.seal(json.toString(), ALIAS)).apply()
        _presets.value = list
    }

    // Empty when nothing is stored or it can't be opened (the key goes with the app's data).
    private fun read(): List<TaskFilterPreset> {
        val text = prefs.getString(KEY, null)?.let { TokenVault.open(it, ALIAS) } ?: return emptyList()
        val array = runCatching { Json.parseToJsonElement(text) as? JsonArray }.getOrNull() ?: return emptyList()
        return array.mapNotNull { element ->
            val item = element as? JsonObject ?: return@mapNotNull null
            fun text(name: String) = (item[name] as? JsonPrimitive)?.contentOrNull
            val id = text("id") ?: return@mapNotNull null
            val name = text("name") ?: return@mapNotNull null
            TaskFilterPreset(id, name, text("category"), text("search").orEmpty())
        }
    }

    private companion object {
        const val KEY = "list"
        const val ALIAS = "accountabilibuddy-task-filter-presets"
        const val MAX_NAME_LENGTH = 40
        const val MAX_PRESETS = 30
    }
}
