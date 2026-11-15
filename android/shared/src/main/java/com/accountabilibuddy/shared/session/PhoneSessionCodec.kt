package com.accountabilibuddy.shared.session

import com.accountabilibuddy.shared.sync.SessionTaskItem
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put
import java.time.Instant

/**
 * The JSON text of `phone-sessions.sealed` and `phone-session-reasons.sealed` before they are sealed (ADR 85, plan section 2). Pure, so the
 * checks run a round trip. A text that is not this format, or is a newer one, reads as null: the caller treats it as unreadable, never as empty (B5).
 * The checklist's task text and the end reasons are private: these texts are only ever written sealed.
 */
object PhoneSessionCodec {
    const val VERSION = 1

    fun write(data: PhoneSessionStoreData): String = buildJsonObject {
        put("version", VERSION)
        data.checkpoint?.let { put("checkpoint", checkpoint(it)) }
        put("log", JsonArray(data.log.map(::record)))
        data.notice?.let { put("notice", notice(it)) }
        data.historyStartsAt?.let { put("historyStartsAt", it.toString()) }
    }.toString()

    /** The store data, or null when [text] can't be read (damaged, or written by a newer version). */
    fun read(text: String): PhoneSessionStoreData? = runCatching {
        val root = Json.parseToJsonElement(text) as JsonObject
        check((root["version"] as? JsonPrimitive)?.intOrNull == VERSION)
        PhoneSessionStoreData(
            checkpoint = (root["checkpoint"] as? JsonObject)?.let(::readCheckpoint),
            log = (root["log"] as? JsonArray).orEmpty().map { readRecord(it as JsonObject) },
            notice = (root["notice"] as? JsonObject)?.let(::readNotice),
            historyStartsAt = root.instant("historyStartsAt"),
        )
    }.getOrNull()

    fun writeReasons(reasons: List<PhoneSessionReason>): String = buildJsonObject {
        put("version", VERSION)
        put("reasons", JsonArray(reasons.map { reason ->
            buildJsonObject {
                put("sessionId", reason.sessionId)
                put("at", reason.at.toString())
                put("text", reason.text)
            }
        }))
    }.toString()

    fun readReasons(text: String): List<PhoneSessionReason>? = runCatching {
        val root = Json.parseToJsonElement(text) as JsonObject
        check((root["version"] as? JsonPrimitive)?.intOrNull == VERSION)
        (root["reasons"] as? JsonArray).orEmpty().map {
            val json = it as JsonObject
            PhoneSessionReason(json.text("sessionId")!!, json.instant("at")!!, json.text("text")!!)
        }
    }.getOrNull()

    // --- writing ----------------------------------------------------------------------------------------------------------------

    private fun checkpoint(value: PhoneSessionCheckpoint): JsonObject = buildJsonObject {
        put("record", record(value.record))
        value.zoneId?.let { put("zone", it) }
        put("startUntrusted", value.startUntrusted)
        put("endUntrusted", value.endUntrusted)
        put("anchor", buildJsonObject {
            put("wall", value.anchor.wall.toString())
            put("elapsedMs", value.anchor.elapsedMs)
            put("bootCount", value.anchor.bootCount)
            put("networkBacked", value.anchor.networkBacked)
        })
    }

    private fun notice(value: PhoneSessionNotice): JsonObject = buildJsonObject {
        put("sessionId", value.sessionId)
        put("from", value.from.toString())
        put("to", value.to.toString())
        put("closed", value.closed)
        put("untrusted", value.untrusted)
    }

    private fun record(r: PhoneSessionRecord): JsonObject = buildJsonObject {
        put("id", r.id)
        put("profileId", r.profileId)
        put("profileName", r.profileName)
        put("startedAt", r.startedAt.toString())
        put("phaseStartedAt", r.phaseStartedAt.toString())
        r.phaseEndsAt?.let { put("phaseEndsAt", it.toString()) }
        put("isTest", r.isTest)
        put("away", JsonArray(r.away.map { away ->
            buildJsonObject {
                put("kind", away.kind.name)
                put("from", away.from.toString())
                away.to?.let { put("to", it.toString()) }
            }
        }))
        put("checklist", buildJsonObject {
            put("tasks", SessionTaskItem.write(r.checklist.tasks) { it.toString() })
            r.checklist.sessionStart?.let { put("sessionStart", it.toString()) }
            put("madeBeforeStart", r.checklist.madeBeforeStart)
            put("droppedOpen", r.checklist.droppedOpen)
            put("droppedOpenIds", JsonArray(r.checklist.droppedOpenIds.map { JsonPrimitive(it) }))
        })
        r.currentTask?.let { put("currentTask", it) }
        put("extensions", r.extensions)
        r.endRequestedAt?.let { put("endRequestedAt", it.toString()) }
        r.armedEnd?.let { armed ->
            put("armedEnd", buildJsonObject {
                put("requestedAt", armed.requestedAt.toString())
                put("readyAt", armed.readyAt.toString())
                put("reason", armed.reason)
            })
        }
        put("heartbeatAt", r.heartbeatAt.toString())
        r.offAt?.let { put("offAt", it.toString()) }
        r.endedAt?.let { put("endedAt", it.toString()) }
        r.endKind?.let { put("endKind", it.name) }
        r.heldAway?.let { put("heldAway", it.name) }
    }

    // --- reading ----------------------------------------------------------------------------------------------------------------

    private fun readCheckpoint(json: JsonObject): PhoneSessionCheckpoint {
        val anchor = json["anchor"] as JsonObject
        return PhoneSessionCheckpoint(
            readRecord(json["record"] as JsonObject),
            PhoneSessionAnchor(
                anchor.instant("wall")!!,
                (anchor["elapsedMs"] as JsonPrimitive).longOrNull!!,
                (anchor["bootCount"] as JsonPrimitive).intOrNull!!,
                (anchor["networkBacked"] as JsonPrimitive).booleanOrNull!!,
            ),
            json.text("zone"),
            json.flag("startUntrusted"),
            json.flag("endUntrusted"),
        )
    }

    private fun readNotice(json: JsonObject) = PhoneSessionNotice(
        json.text("sessionId")!!, json.instant("from")!!, json.instant("to")!!,
        json.flag("closed"), json.flag("untrusted"),
    )

    private fun readRecord(json: JsonObject): PhoneSessionRecord {
        val checklist = json["checklist"] as JsonObject
        return PhoneSessionRecord(
            id = json.text("id")!!,
            profileId = json.text("profileId")!!,
            profileName = json.text("profileName")!!,
            startedAt = json.instant("startedAt")!!,
            phaseStartedAt = json.instant("phaseStartedAt")!!,
            phaseEndsAt = json.instant("phaseEndsAt"),
            isTest = json.flag("isTest"),
            away = (json["away"] as JsonArray).map {
                val away = it as JsonObject
                PhoneAway(PhoneAwayKind.valueOf(away.text("kind")!!), away.instant("from")!!, away.instant("to"))
            },
            checklist = PhoneSessionChecklist(
                tasks = SessionTaskItem.read(checklist["tasks"] as JsonArray),
                sessionStart = checklist.instant("sessionStart"),
                madeBeforeStart = checklist.flag("madeBeforeStart"),
                droppedOpen = (checklist["droppedOpen"] as JsonPrimitive).intOrNull!!,
                droppedOpenIds = (checklist["droppedOpenIds"] as JsonArray).map { (it as JsonPrimitive).contentOrNull!! },
            ),
            currentTask = json.text("currentTask"),
            extensions = (json["extensions"] as JsonPrimitive).intOrNull!!,
            endRequestedAt = json.instant("endRequestedAt"),
            armedEnd = (json["armedEnd"] as? JsonObject)?.let { PhoneArmedEnd(it.instant("requestedAt")!!, it.instant("readyAt")!!, it.text("reason")!!) },
            heartbeatAt = json.instant("heartbeatAt")!!,
            offAt = json.instant("offAt"),
            endedAt = json.instant("endedAt"),
            endKind = json.text("endKind")?.let { PhoneSessionEndKind.valueOf(it) },
            heldAway = json.text("heldAway")?.let { PhoneAwayKind.valueOf(it) },
        )
    }

    private fun JsonObject.text(key: String): String? = (this[key] as? JsonPrimitive)?.takeIf { it !is JsonNull }?.contentOrNull

    private fun JsonObject.instant(key: String): Instant? = text(key)?.let { Instant.parse(it) }

    private fun JsonObject.flag(key: String): Boolean = ((this[key] as? JsonPrimitive)?.booleanOrNull) == true
}
