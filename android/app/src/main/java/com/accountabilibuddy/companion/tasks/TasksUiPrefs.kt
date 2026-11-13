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
import java.time.ZoneOffset
import com.accountabilibuddy.shared.tasks.TaskHideShelf
import com.accountabilibuddy.shared.tasks.TaskHideShelfResult
import com.accountabilibuddy.shared.tasks.TaskHideShelfRule

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

    /** Asks for the Shelf to hide (stricter, at once) or show in a session (looser: waits, never in a session); what happened ([TaskHideShelfRule]). */
    @Synchronized
    fun setHideShelf(hide: Boolean, env: ShelfEnvironment): TaskHideShelfResult {
        var result = TaskHideShelfResult.Unchanged
        update { state ->
            val held = TaskHideShelf(state.hideShelfInSession, state.showShelfAt?.let { OffsetDateTime.ofInstant(it, ZoneOffset.UTC) })
            val settled = TaskHideShelfRule.settle(held, env.productaBuddy, env.sessionRunning, env.now)
            val (next, outcome) = TaskHideShelfRule.set(settled, hide, env.productaBuddy, env.sessionRunning, env.now, env.delay)
            result = outcome
            state.copy(hideShelfInSession = next.hide, showShelfAt = next.showAt?.toInstant())
        }
        return result
    }

    /** Cancel on a waiting "show the Shelf". */
    @Synchronized
    fun cancelShowShelf() = update { it.copy(showShelfAt = null) }

    /** A waiting "show the Shelf" whose time has come applies (no session running, or ProductaBuddy mode). */
    @Synchronized
    fun settleHideShelf(env: ShelfEnvironment) = update { state ->
        val held = TaskHideShelf(state.hideShelfInSession, state.showShelfAt?.let { OffsetDateTime.ofInstant(it, ZoneOffset.UTC) })
        val settled = TaskHideShelfRule.settle(held, env.productaBuddy, env.sessionRunning, env.now)
        state.copy(hideShelfInSession = settled.hide, showShelfAt = settled.showAt?.toInstant())
    }

    /** What the looser-change rule needs: the mode, a running session, the trusted time and the wait. */
    data class ShelfEnvironment(val productaBuddy: Boolean, val sessionRunning: Boolean, val now: OffsetDateTime, val delay: java.time.Duration)

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
                showShelfAt = (json["showShelfAt"] as? JsonPrimitive)?.content?.let { text -> runCatching { java.time.Instant.parse(text) }.getOrNull() },
                noticeDismissedAt = (json["noticeAt"] as? JsonPrimitive)?.content?.let { text -> runCatching { java.time.Instant.parse(text) }.getOrNull() },
                noticeDismissedFor = (json["noticeFor"] as? JsonArray).orEmpty().mapNotNull { (it as? JsonPrimitive)?.content },
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
        state.showShelfAt?.let { put("showShelfAt", it.toString()) }
        // The "Update AccountabiliBuddy on" notice: when it was closed and for which devices (their ids only).
        state.noticeDismissedAt?.let { put("noticeAt", it.toString()) }
        if (state.noticeDismissedFor.isNotEmpty()) put("noticeFor", JsonArray(state.noticeDismissedFor.map { JsonPrimitive(it) }))
    }

    companion object {
        private const val PREFS = "tasks_ui"
        private const val KEY = "state"
        private const val ALIAS = "accountabilibuddy-task-model"
        /** The environment read when a change is asked or settled: trusted time (ADR 70), the mode, a session the user started, the wait. */
        fun environment(context: Context): ShelfEnvironment {
            val trusted = com.accountabilibuddy.companion.guidance.TrustedTime.now()
            val app = context.applicationContext as com.accountabilibuddy.companion.CompanionApp
            return ShelfEnvironment(
                productaBuddy = app.guidance.state.value.mode == com.accountabilibuddy.companion.guidance.GuidanceMode.Companion,
                sessionRunning = com.accountabilibuddy.companion.placement.GuardsLock.at(app, trusted) != null,
                now = OffsetDateTime.ofInstant(trusted, ZoneOffset.UTC),
                delay = com.accountabilibuddy.companion.guidance.GuidanceStore.DELAY,
            )
        }

        private val EPOCH: OffsetDateTime = OffsetDateTime.parse("2000-01-01T00:00:00Z")

        @Volatile
        private var instance: TasksUiPrefs? = null

        /** The one store of the app. */
        fun of(context: Context): TasksUiPrefs = instance ?: synchronized(this) { instance ?: TasksUiPrefs(context).also { instance = it } }
    }
}
