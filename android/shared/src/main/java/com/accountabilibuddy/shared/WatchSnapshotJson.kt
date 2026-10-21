package com.accountabilibuddy.shared

import com.accountabilibuddy.shared.FocusDayJson.bool
import com.accountabilibuddy.shared.FocusDayJson.date
import com.accountabilibuddy.shared.FocusDayJson.instant
import com.accountabilibuddy.shared.FocusDayJson.int
import com.accountabilibuddy.shared.FocusDayJson.objects
import com.accountabilibuddy.shared.FocusDayJson.string
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

/** The watch snapshot as JSON: what the phone sends in a "/snapshot" message and what the watch keeps as a file. */
object WatchSnapshotJson {
    fun write(snapshot: WatchSnapshot): JsonObject = buildJsonObject {
        put("createdAt", snapshot.createdAt.toString())
        put("linked", snapshot.linked)
        snapshot.pcName?.let { put("pcName", it) }
        put("date", snapshot.date.toString())
        put("parts", buildJsonArray { snapshot.parts.forEach { add(FocusDayJson.writePart(it)) } })
        put("sessions", buildJsonArray { snapshot.sessions.forEach { add(FocusDayJson.writeSession(it)) } })
        put("drift", buildJsonArray { snapshot.drift.forEach { add(FocusDayJson.writeWindow(it)) } })
        put("plannedMinutes", snapshot.plannedMinutes)
        put("week", buildJsonArray { snapshot.week.forEach { add(FocusDayJson.writeTotal(it)) } })
        snapshot.running?.let { running ->
            put("running", buildJsonObject {
                running.profile?.let { put("profile", it) }
                put("startedAt", running.startedAt.toString())
                running.endsAt?.let { put("endsAt", it.toString()) }
                put("onBreak", running.onBreak)
            })
        }
    }

    /** Null when it isn't a snapshot (no creation time or date). */
    fun read(json: JsonObject): WatchSnapshot? {
        val createdAt = json.instant("createdAt") ?: return null
        val date = json.date("date") ?: return null
        return WatchSnapshot(
            createdAt = createdAt,
            linked = json.bool("linked"),
            pcName = json.string("pcName"),
            date = date,
            parts = json.objects("parts").mapNotNull(FocusDayJson::readPart),
            sessions = json.objects("sessions").mapNotNull(FocusDayJson::readSession),
            drift = json.objects("drift").mapNotNull(FocusDayJson::readWindow),
            plannedMinutes = json.int("plannedMinutes") ?: 0,
            week = json.objects("week").mapNotNull(FocusDayJson::readTotal),
            running = (json["running"] as? JsonObject)?.let(::running),
        )
    }

    private fun running(json: JsonObject): RunningSession? {
        val startedAt = json.instant("startedAt") ?: return null
        return RunningSession(json.string("profile"), startedAt, json.instant("endsAt"), json.bool("onBreak"))
    }
}
