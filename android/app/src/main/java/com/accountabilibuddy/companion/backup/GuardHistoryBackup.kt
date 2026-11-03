package com.accountabilibuddy.companion.backup

import android.content.Context
import com.accountabilibuddy.companion.blocks.BlockGuard
import com.accountabilibuddy.companion.nudge.EmergencyStore
import com.accountabilibuddy.companion.nudge.EmergencyUse
import com.accountabilibuddy.companion.nudge.NudgeStore
import com.accountabilibuddy.companion.placement.PhoneDownStore
import com.accountabilibuddy.companion.placement.WorkUse
import com.accountabilibuddy.companion.placement.WorkUseKind
import com.accountabilibuddy.companion.placement.WorkUseStore
import com.accountabilibuddy.companion.sync.OverLimitStore
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/**
 * The phone's guard history in a backup: when emergency time and work uses ran, when the Phone down screen showed, when a 4th task was added, the
 * block guard's log and today's snoozes. Times and kinds only: the reasons and notes stay on the phone, because they never leave a device and a
 * backup is a plain file. Restoring merges into what the phone holds, so it can only add to today's counts; a use counts by its own start time, so
 * an older backup's uses fall into past days by themselves.
 */
object GuardHistoryBackup {
    fun export(context: Context): JsonObject {
        val nudge = NudgeStore(context).load()
        return buildJsonObject {
            put("emergencyUses", buildJsonArray {
                EmergencyStore(context).all().forEach { use ->
                    add(buildJsonObject { put("start", use.start.toString()); put("minutes", use.minutes) })
                }
            })
            put("workUses", buildJsonArray {
                WorkUseStore(context).all().forEach { use ->
                    add(buildJsonObject {
                        put("session", use.sessionStart.toString())
                        put("start", use.start.toString())
                        put("end", use.end.toString())
                        put("kind", use.kind.name)
                    })
                }
            })
            put("phoneDownShown", buildJsonArray { PhoneDownStore(context).shownLog().forEach { add(JsonPrimitive(it)) } })
            put("overLimitAt", buildJsonArray { OverLimitStore(context).times().forEach { add(JsonPrimitive(it.toString())) } })
            put("blockHistory", buildJsonArray { BlockGuard.history(context).forEach { add(JsonPrimitive(it)) } })
            nudge.snoozeDay?.let { day ->
                put("snoozeDay", day.toString())
                put("snoozes", nudge.snoozes)
            }
        }
    }

    /** True when [history] holds a use or snooze from [today]: leaving it out on a restore would reset today's allowance, so it can't be. */
    fun holdsToday(history: JsonObject?, today: LocalDate, zone: ZoneId = ZoneId.systemDefault()): Boolean {
        history ?: return false
        fun on(array: String) = (history[array] as? JsonArray).orEmpty().any { entry ->
            val start = (entry as? JsonObject)?.get("start")?.jsonPrimitive?.contentOrNull
            start != null && runCatching { Instant.parse(start).atZone(zone).toLocalDate() == today }.getOrDefault(false)
        }

        val snoozedToday = history["snoozeDay"]?.jsonPrimitive?.contentOrNull == today.toString() && (history["snoozes"]?.jsonPrimitive?.intOrNull ?: 0) > 0
        return on("emergencyUses") || on("workUses") || snoozedToday
    }

    fun merge(context: Context, history: JsonObject) {
        fun array(name: String) = (history[name] as? JsonArray).orEmpty()

        EmergencyStore(context).mergeRestored(array("emergencyUses").mapNotNull { entry ->
            runCatching {
                val json = entry.jsonObject
                EmergencyUse(RESTORED_REASON, Instant.parse(json.getValue("start").jsonPrimitive.content), json.getValue("minutes").jsonPrimitive.intOrNull!!)
            }.getOrNull()
        })
        WorkUseStore(context).mergeRestored(array("workUses").mapNotNull { entry ->
            runCatching {
                val json = entry.jsonObject
                WorkUse(
                    Instant.parse(json.getValue("session").jsonPrimitive.content),
                    Instant.parse(json.getValue("start").jsonPrimitive.content),
                    Instant.parse(json.getValue("end").jsonPrimitive.content),
                    WorkUseKind.valueOf(json.getValue("kind").jsonPrimitive.content),
                    RESTORED_REASON,
                )
            }.getOrNull()
        })
        PhoneDownStore(context).mergeRestoredShown(array("phoneDownShown").mapNotNull { it.jsonPrimitive.longOrNull })
        OverLimitStore(context).mergeRestored(array("overLimitAt").mapNotNull { runCatching { Instant.parse(it.jsonPrimitive.content) }.getOrNull() }, RESTORED_REASON)
        BlockGuard.mergeRestoredHistory(context, array("blockHistory").mapNotNull { it.jsonPrimitive.contentOrNull })

        val snoozeDay = history["snoozeDay"]?.jsonPrimitive?.contentOrNull?.let { runCatching { LocalDate.parse(it) }.getOrNull() }
        val snoozes = history["snoozes"]?.jsonPrimitive?.intOrNull ?: 0
        if (snoozeDay == LocalDate.now()) {
            NudgeStore(context).update { state ->
                val held = if (state.snoozeDay == snoozeDay) state.snoozes else 0
                state.copy(snoozeDay = snoozeDay, snoozes = maxOf(held, snoozes))
            }
        }

        // The stores write in the background and the app restarts right after a restore: a commit waits for what they queued.
        STORES.forEach { context.getSharedPreferences(it, Context.MODE_PRIVATE).edit().commit() }
    }

    private val STORES = listOf("emergency", "workUse", "phoneDown", "over_limit", "blockGuard", "takeover")

    private const val RESTORED_REASON = "(reason not in backup)"
}
