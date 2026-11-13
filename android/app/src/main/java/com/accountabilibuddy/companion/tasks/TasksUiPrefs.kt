package com.accountabilibuddy.companion.tasks

import android.content.Context
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskView
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.time.OffsetDateTime

/**
 * Keeps [TasksUiState] in the app's private prefs (`tasks_ui`), sealed with a key in the Android Keystore like the task list itself:
 * a page order names tags, which are the user's private text. Device-local: it is in no sync file and no backup.
 */
class TasksUiPrefs private constructor(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
    private val _state = MutableStateFlow(read())

    val state: StateFlow<TasksUiState> = _state.asStateFlow()

    /** Changes what is remembered and keeps it. */
    @Synchronized
    fun update(change: (TasksUiState) -> TasksUiState) {
        val next = change(_state.value)
        if (next == _state.value) return
        _state.value = next
        prefs.edit().putString(KEY, TokenVault.seal(write(next).toString(), ALIAS)).apply()
    }

    private fun read(): TasksUiState {
        val text = prefs.getString(KEY, null)?.let { TokenVault.open(it, ALIAS) } ?: return TasksUiState()
        return runCatching {
            val json = Json.parseToJsonElement(text) as JsonObject
            val view = json["view"]?.let { TaskJson.readView(it) }
            TasksUiState(
                config = view?.config ?: TasksUiState().config,
                viewId = (json["viewId"] as? JsonPrimitive)?.takeIf { it.isString }?.content,
                orders = (json["order"] as? JsonObject).orEmpty().mapValues { (_, keys) -> (keys as? JsonArray).orEmpty().mapNotNull { (it as? JsonPrimitive)?.content } },
                starts = (json["start"] as? JsonObject).orEmpty().mapNotNull { (grouping, key) -> (key as? JsonPrimitive)?.content?.let { grouping to it } }.toMap(),
                wraps = (json["wrap"] as? JsonObject).orEmpty().mapNotNull { (grouping, on) -> (on as? JsonPrimitive)?.booleanOrNull?.let { grouping to it } }.toMap(),
                hideShelfInSession = (json["hideShelf"] as? JsonPrimitive)?.booleanOrNull ?: true,
            )
        }.getOrDefault(TasksUiState())
    }

    private fun write(state: TasksUiState): JsonObject = buildJsonObject {
        // The settings travel in a view's own JSON shape, so they read back exactly as a saved view's would.
        put("view", TaskJson.writeView(TaskView(TaskView.DEFAULT_ID, "", null, state.config, 0.0, EPOCH)))
        state.viewId?.let { put("viewId", it) }
        put("order", JsonObject(state.orders.mapValues { (_, keys) -> JsonArray(keys.map { JsonPrimitive(it) }) }))
        put("start", JsonObject(state.starts.mapValues { JsonPrimitive(it.value) }))
        put("wrap", JsonObject(state.wraps.mapValues { JsonPrimitive(it.value) }))
        put("hideShelf", state.hideShelfInSession)
    }

    companion object {
        private const val PREFS = "tasks_ui"
        private const val KEY = "state"
        private const val ALIAS = "accountabilibuddy-task-model"
        private val EPOCH: OffsetDateTime = OffsetDateTime.parse("2000-01-01T00:00:00Z")

        @Volatile
        private var instance: TasksUiPrefs? = null

        /** The one store of the app. */
        fun of(context: Context): TasksUiPrefs = instance ?: synchronized(this) { instance ?: TasksUiPrefs(context).also { instance = it } }
    }
}
