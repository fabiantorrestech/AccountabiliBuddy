package com.accountabilibuddy.companion.sync

import android.content.Context
import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncActionResult
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.put
import java.time.Instant

/**
 * "Add to Today" while a PC runs a session: the Later item or come-back is sent to the PC's checklist as a `taskAdd` action, and it
 * leaves its list here only when the PC really took the whole task:
 *
 * - A refusal leaves it where it was (the PC's message is shown).
 * - A PC that is at its limit parks the task as a come-back instead, which carries only its title, category and a step count: a
 *   Later item then stays, with its note, steps, estimate and due date, and the phone says so. (A PC that predates the `parked`
 *   status answers `ok` with a message; that counts as parked too.)
 * - A Later item that was edited here after it was sent stays too, so the edit is not lost.
 *
 * The hold is kept in the app's prefs (ids and a time only, never text) and lasts as long as the action is pending, so a restart
 * between send and answer neither loses it nor lets the same item be sent twice ([isWaiting]).
 */
class TodayHandoff(context: Context, private val lists: SharedListsStore, private val tell: (String) -> Unit) {
    /** Where a sent task came from. */
    sealed interface Source {
        val id: String

        data class Later(override val id: String) : Source

        data class ComeBack(override val id: String) : Source
    }

    private data class Entry(val taskId: String, val source: Source, val editedAt: Instant?)

    private val prefs = context.getSharedPreferences("today_handoff", Context.MODE_PRIVATE)
    private var entries: List<Entry> = read()

    /** The `taskAdd` for [taskId] is on its way. [editedAt] is the Later item's stamp as sent (null for a come-back). */
    @Synchronized
    fun hold(taskId: String, source: Source, editedAt: Instant?) {
        entries = entries.filter { it.taskId != taskId } + Entry(taskId, source, editedAt)
        save()
    }

    /** True while an add of [source] waits for its PC's answer ([pending] is the phone's pending actions). */
    @Synchronized
    fun isWaiting(source: Source, pending: List<SyncAction>): Boolean = entries.any { it.source == source && pending.any { action -> action.item == it.taskId } }

    /** The ids of the sources whose add still waits for a PC's answer. */
    @Synchronized
    fun waitingIds(pending: List<SyncAction>): Set<String> = entries.filter { entry -> pending.any { it.item == entry.taskId } }.map { it.source.id }.toSet()

    /** A PC answered [action] ([result] is null from a PC that acks without answering, which is not taken as a yes). */
    fun answered(action: SyncAction, result: SyncActionResult?, pcName: String) {
        if (action.kind != SyncAction.TASK_ADD) return
        val entry = synchronized(this) {
            val found = entries.firstOrNull { it.taskId == action.item } ?: return
            entries = entries.filter { it !== found }
            save()
            found
        }
        if (result?.ok != true) return
        val parked = result.status == SyncActionResult.STATUS_PARKED || result.message != null
        when (val source = entry.source) {
            is Source.ComeBack -> lists.tickParked(source.id)
            is Source.Later -> {
                val held = lists.later.value.firstOrNull { it.id == source.id } ?: return
                when {
                    parked -> tell("$pcName parked it as a come-back instead (its list is full). It stays in Later with its note, steps and dates.")
                    entry.editedAt != null && held.editedAt != entry.editedAt ->
                        tell("$pcName took it, but it was changed here after it was sent, so it stays in Later too.")
                    else -> lists.deleteLater(source.id)
                }
            }
        }
    }

    private fun save() {
        val json = JsonArray(entries.map { entry ->
            buildJsonObject {
                put("taskId", entry.taskId)
                put("kind", if (entry.source is Source.Later) "later" else "comeBack")
                put("id", entry.source.id)
                entry.editedAt?.let { put("editedAt", it.toString()) }
            }
        })
        prefs.edit().putString(KEY, json.toString()).apply()
    }

    private fun read(): List<Entry> = runCatching {
        val text = prefs.getString(KEY, null) ?: return emptyList()
        (Json.parseToJsonElement(text) as JsonArray).mapNotNull { element ->
            val item = element as? JsonObject ?: return@mapNotNull null
            fun text(name: String) = (item[name] as? JsonPrimitive)?.contentOrNull
            val id = text("id") ?: return@mapNotNull null
            val source = if (text("kind") == "later") Source.Later(id) else Source.ComeBack(id)
            Entry(text("taskId") ?: return@mapNotNull null, source, text("editedAt")?.let { runCatching { Instant.parse(it) }.getOrNull() })
        }
    }.getOrDefault(emptyList())

    private companion object {
        const val KEY = "waiting"
    }
}
