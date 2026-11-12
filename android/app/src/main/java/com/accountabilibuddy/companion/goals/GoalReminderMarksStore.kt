package com.accountabilibuddy.companion.goals

import android.content.Context
import com.accountabilibuddy.shared.goals.GoalReminderMark
import com.accountabilibuddy.shared.goals.GoalReminderMarkKind
import com.accountabilibuddy.shared.goals.GoalReminderState
import java.time.Instant
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put

/**
 * Goal reminders' and the weekly review's ringing state on this phone (round 2 R6b3; the PC's "goals.reminderState"): each occurrence's
 * mark (shown, snoozed, dismissed, skipped) by its key. Ids and times only, never a goal name or reason; plain preferences
 * "goalReminderState", state of the moment: never synced, never in a backup. A damaged value reads as no marks.
 */
class GoalReminderMarksStore(context: Context) {
    private val prefs = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)

    @Synchronized
    fun load(): GoalReminderState = runCatching {
        val text = prefs.getString(KEY, null) ?: return GoalReminderState.EMPTY
        val root = Json.parseToJsonElement(text).jsonObject
        GoalReminderState(
            root.mapValues { (_, value) ->
                val mark = value.jsonObject
                GoalReminderMark(
                    kind = GoalReminderMarkKind.valueOf(mark.getValue("k").jsonPrimitive.content),
                    at = Instant.ofEpochSecond(mark.getValue("at").jsonPrimitive.longOrNull ?: 0L),
                    snoozedUntil = mark["until"]?.jsonPrimitive?.longOrNull?.let(Instant::ofEpochSecond),
                    snoozeCount = mark["n"]?.jsonPrimitive?.intOrNull ?: 0,
                )
            },
        )
    }.getOrDefault(GoalReminderState.EMPTY)

    /** Marks [key] (pruning old marks) and saves at once. */
    @Synchronized
    fun mark(key: String, mark: GoalReminderMark, now: Instant): GoalReminderState {
        val state = load().with(key, mark, now)
        save(state)
        return state
    }

    /** Marks several keys at once (skips found together). */
    @Synchronized
    fun markAll(keys: List<String>, kind: GoalReminderMarkKind, now: Instant) {
        if (keys.isEmpty()) return
        save(keys.fold(load()) { state, key -> state.with(key, GoalReminderMark(kind, now), now) })
    }

    private fun save(state: GoalReminderState) {
        val root = JsonObject(
            state.marks.mapValues { (_, mark) ->
                buildJsonObject {
                    put("k", mark.kind.name)
                    put("at", mark.at.epochSecond)
                    mark.snoozedUntil?.let { put("until", it.epochSecond) }
                    if (mark.snoozeCount > 0) put("n", mark.snoozeCount)
                }
            },
        )
        prefs.edit().putString(KEY, root.toString()).apply()
    }

    companion object {
        const val FILE = "goalReminderState"
        private const val KEY = "marks"
    }
}
