package com.accountabilibuddy.shared.sync

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.put
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.time.format.DateTimeFormatterBuilder
import java.time.format.DateTimeParseException
import java.time.temporal.ChronoField
import java.util.Locale
import java.util.UUID

/**
 * A [SyncFile] as JSON, in the PC's shape (System.Text.Json with LinkFraming.JsonOptions): the same camelCase names in
 * the same order, nulls written (except "coversFrom", written last and only when set), "seen" as an object keyed by
 * device id, and times as .NET writes a DateTimeOffset ("2026-10-21T20:30:00.25+00:00": seconds always, a fraction of up
 * to 7 digits only when there is one, UTC as "+00:00"). Reading takes any ISO time with an offset, "Z" too.
 *
 * Reading refuses what the PC refuses: a missing or null list, card field, drift app, plan field or document field, a
 * value of the wrong type, the empty device id, a device id not in the 36-character form, a format version outside 1 to
 * [SyncFile.CURRENT_FORMAT_VERSION]. A missing time is refused too (the PC would read it as year 1), except the optional
 * coversFrom. Unknown properties are ignored, and unknown kinds, methods and document keys are kept (a newer app within
 * format 1).
 */
object SyncFileJson {
    // .NET keeps 100 ns ticks: a finer fraction is cut, as the PC would keep it.
    private val dotNetTime: DateTimeFormatter = DateTimeFormatterBuilder()
        .appendPattern("uuuu-MM-dd'T'HH:mm:ss")
        .appendFraction(ChronoField.NANO_OF_SECOND, 0, 7, true)
        .appendOffset("+HH:MM", "+00:00")
        .toFormatter(Locale.ROOT)

    private val deviceIdForm = Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
    private val emptyDeviceId = UUID(0, 0)

    fun write(file: SyncFile): JsonObject = buildJsonObject {
        put("formatVersion", file.formatVersion)
        put("device", writeDevice(file.device))
        put("sessions", JsonArray(file.sessions.map(::writeSession)))
        put("drift", JsonArray(file.drift.map(::writeDrift)))
        put("dayPlan", file.dayPlan?.let(::writeDayPlan) ?: JsonNull)
        put("documents", JsonArray(file.documents.map(::writeDocument)))
        file.coversFrom?.let { put("coversFrom", writeTime(it)) }
        file.comebacks?.let { list ->
            put("comebacks", JsonArray(list.map { comeback ->
                buildJsonObject {
                    put("id", comeback.id)
                    put("text", comeback.text)
                    put("createdAt", writeTime(comeback.createdAt))
                }
            }))
        }
        file.actions?.let { list ->
            put("actions", JsonArray(list.map { action ->
                buildJsonObject {
                    put("id", action.id)
                    put("kind", action.kind)
                    put("target", action.target.toString())
                    action.item?.let { put("item", it) }
                    action.text?.let { put("text", it) }
                    put("at", writeTime(action.at))
                }
            }))
        }
        file.acks?.let { list -> put("acks", JsonArray(list.map { kotlinx.serialization.json.JsonPrimitive(it) })) }
        file.events?.let { put("events", it) }
        // Optional (ADR 37): this device's blocks.
        file.blockers?.let { list ->
            put("blockers", JsonArray(list.map { b ->
                buildJsonObject {
                    put("kind", b.kind)
                    put("id", b.id)
                    put("name", b.name)
                    put("on", b.on)
                    put("locked", b.locked)
                }
            }))
        }
        // Optional (ADR 38): bedtime reminders dismissed or snoozed.
        file.reminderActions?.takeIf { it.isNotEmpty() }?.let { list ->
            put("reminderActions", JsonArray(list.map { com.accountabilibuddy.shared.ReminderMessages.toJson(it) }))
        }
        // Optional (ADR 34): a phone's smart bedtime.
        file.smartBedtime?.let { smart ->
            put("smartBedtime", buildJsonObject {
                smart.workdays?.let { put("workdays", writeSmartLevel(it)) }
                smart.offDays?.let { put("offDays", writeSmartLevel(it)) }
            })
        }
        // Optional (ADR 40): a phone's estimated wake times.
        file.wakeEstimate?.let { estimate ->
            put("wakeEstimate", buildJsonObject {
                estimate.workdays?.let { put("workdays", it) }
                estimate.offDays?.let { put("offDays", it) }
            })
        }
        // Optional (ADR 43): a PC's Beeminder summary. The phone never has one of its own; kept for a faithful round trip.
        file.beeminder?.let { list ->
            put("beeminder", JsonArray(list.map { goal ->
                buildJsonObject {
                    put("title", goal.title)
                    put("needed", goal.needed)
                    put("safeDays", goal.safeDays)
                    put("dueAt", writeTime(goal.dueAt))
                }
            }))
        }
        file.running?.let { running ->
            put("running", buildJsonObject {
                running.profile?.let { put("profile", it) }
                put("phaseStartedAt", writeTime(running.phaseStartedAt))
                running.phaseEndsAt?.let { put("phaseEndsAt", writeTime(it)) }
                put("isBreak", running.isBreak)
                running.awaySince?.let { put("awaySince", writeTime(it)) }
                // Optional (ADR 42).
                running.currentTask?.let { put("currentTask", it) }
                running.profileId?.let { put("profileId", it) }
                running.countUpProfileId?.let { put("countUpProfileId", it) }
                // Optional (phase 11): the PC's planning stage.
                running.planningStartedAt?.let { put("planningStartedAt", writeTime(it)) }
                running.planningEndsAt?.let { put("planningEndsAt", writeTime(it)) }
            })
        }
        // Optional (ADR 42): a PC's answers, its end-early offer, its goal and its profiles for the start picker.
        file.actionResults?.let { list ->
            put("actionResults", JsonArray(list.map { result ->
                buildJsonObject {
                    put("id", result.id)
                    put("ok", result.ok)
                    put("status", result.status)
                    result.message?.let { put("message", it) }
                    result.planVersion?.let { put("planVersion", it) }
                    result.planEditedAt?.let { put("planEditedAt", writeTime(it)) }
                }
            }))
        }
        file.endEarly?.let { offer ->
            put("endEarly", buildJsonObject {
                put("requestId", offer.requestId)
                put("phaseStartedAt", writeTime(offer.phaseStartedAt))
                put("allowedFrom", writeTime(offer.allowedFrom))
                put("minReasonLength", offer.minReasonLength)
                offer.note?.let { put("note", it) }
            })
        }
        file.goalMinutes?.let { put("goalMinutes", it) }
        file.profileOptions?.let { list ->
            put("profileOptions", JsonArray(list.map { option ->
                buildJsonObject {
                    put("id", option.id)
                    put("name", option.name)
                }
            }))
        }
        file.defaultProfileId?.let { put("defaultProfileId", it) }
        // Optional (ADR 42, 36): a PC's noise player.
        file.noise?.let { noise ->
            put("noise", buildJsonObject {
                put("playing", noise.playing)
                noise.name?.let { put("name", it) }
                put("volume", noise.volume)
                put("locked", noise.locked)
                put("at", writeTime(noise.at))
            })
        }
        file.lunch?.let { lunch ->
            put("lunch", buildJsonObject {
                put("start", writeTime(lunch.start))
                put("end", writeTime(lunch.end))
                lunch.editedAt?.let { put("editedAt", writeTime(it)) }
            })
        }
        file.pickups?.let { list ->
            put("pickups", JsonArray(list.map { pickup ->
                buildJsonObject {
                    put("deviceId", pickup.deviceId.toString())
                    put("sessionStart", writeTime(pickup.sessionStart))
                    put("count", pickup.count)
                    put("longestDownMinutes", pickup.longestDownMinutes)
                    put("countedUntil", writeTime(pickup.countedUntil))
                    if (pickup.emergencyMinutes > 0) put("emergencyMinutes", pickup.emergencyMinutes)
                }
            }))
        }
    }

    /** Null when the JSON isn't a complete sync file this app can read (see the class comment). */
    fun read(json: JsonObject): SyncFile? = try {
        readFile(json)
    } catch (e: NotASyncFileException) {
        null
    }

    private fun writeDevice(device: SyncDeviceCard): JsonObject = buildJsonObject {
        put("deviceId", device.deviceId.toString())
        put("name", device.name)
        put("kind", device.kind)
        put("appVersion", device.appVersion)
        put("writtenAt", writeTime(device.writtenAt))
        put("methods", JsonArray(device.methods.map { JsonPrimitive(it) }))
        put("historyOff", device.historyOff)
        put("seen", JsonObject(device.seen.entries.associate { (id, seen) -> id.toString() to JsonPrimitive(writeTime(seen)) }))
        // Optional (Task 18): written only when there are any, so a file without them is format 1 as before.
        if (device.companions.isNotEmpty()) {
            put("companions", JsonArray(device.companions.map { companion ->
                buildJsonObject {
                    put("name", companion.name)
                    put("kind", companion.kind)
                    put("linked", companion.linked)
                    put("lastHeard", companion.lastHeard?.let(::writeTime))
                }
            }))
        }

        // Optional (ADR 23), like the companions.
        device.nextAlarm?.let { put("nextAlarm", writeTime(it)) }
    }

    private fun writeSession(session: SyncSession): JsonObject = buildJsonObject {
        put("start", writeTime(session.start))
        put("end", session.end?.let(::writeTime))
        put("profile", session.profile)
        put("added", session.added)
        if (session.tasks.isNotEmpty()) {
            put("tasks", SessionTaskItem.write(session.tasks, ::writeTime))
        }
    }

    private fun writeDrift(drift: SyncDrift): JsonObject = buildJsonObject {
        put("start", writeTime(drift.start))
        put("end", writeTime(drift.end))
        put("app", drift.app)
        put("site", drift.site)
        put("section", drift.section)
    }

    private fun writeDayPlan(plan: SyncDayPlan): JsonObject = buildJsonObject {
        put("planJson", plan.planJson)
        put("version", plan.version)
        put("editedAt", writeTime(plan.editedAt))
        put("basedOn", plan.basedOn)
    }

    private fun writeDocument(document: SyncDocument): JsonObject = buildJsonObject {
        put("key", document.key)
        put("json", document.json)
        put("editedAt", writeTime(document.editedAt))
    }

    private fun writeTime(time: Instant): String = dotNetTime.format(time.atOffset(ZoneOffset.UTC))

    private fun readFile(json: JsonObject): SyncFile {
        val formatVersion = json.requiredInt("formatVersion")
        if (formatVersion < 1 || formatVersion > SyncFile.CURRENT_FORMAT_VERSION) {
            throw NotASyncFileException()
        }

        return SyncFile(
            formatVersion,
            readDevice(json.requiredObject("device")),
            json.requiredObjects("sessions").map(::readSession),
            json.requiredObjects("drift").map(::readDrift),
            json.optionalObject("dayPlan")?.let(::readDayPlan),
            json.requiredObjects("documents").map(::readDocument),
            json.optionalTime("coversFrom"),
            readComebacks(json["comebacks"]),
            readPickups(json["pickups"]),
            json.optionalObject("lunch")?.let { lunch -> runCatching { SyncLunch(lunch.requiredTime("start"), lunch.requiredTime("end"), lunch.optionalTime("editedAt")) }.getOrNull() },
            readActions(json["actions"]),
            (json["acks"] as? JsonArray)?.mapNotNull { (it as? kotlinx.serialization.json.JsonPrimitive)?.takeIf { p -> p.isString }?.content },
            json.optionalObject("running")?.let { running ->
                runCatching {
                    SyncRunning(
                        (running["profile"] as? kotlinx.serialization.json.JsonPrimitive)?.takeIf { it.isString }?.content,
                        running.requiredTime("phaseStartedAt"),
                        running.optionalTime("phaseEndsAt"),
                        (running["isBreak"] as? kotlinx.serialization.json.JsonPrimitive)?.content == "true",
                        running.optionalTime("awaySince"),
                        optionalText(running, "currentTask"),
                        optionalText(running, "profileId"),
                        optionalText(running, "countUpProfileId"),
                        running.optionalTime("planningStartedAt"),
                        running.optionalTime("planningEndsAt"),
                    )
                }.getOrNull()
            },
            json["events"] as? JsonArray,
            json.optionalObject("smartBedtime")?.let { smart ->
                SyncSmartBedtime(smart.optionalObject("workdays")?.let(::readSmartLevel), smart.optionalObject("offDays")?.let(::readSmartLevel))
            },
            (json["reminderActions"] as? JsonArray)?.mapNotNull { (it as? JsonObject)?.let(com.accountabilibuddy.shared.ReminderMessages::fromJson) },
            (json["blockers"] as? JsonArray)?.mapNotNull { element ->
                val b = element as? JsonObject ?: return@mapNotNull null
                fun text(key: String) = (b[key] as? JsonPrimitive)?.content
                SyncBlocker(
                    text("kind") ?: return@mapNotNull null,
                    text("id") ?: return@mapNotNull null,
                    text("name") ?: "",
                    text("on") == "true",
                    text("locked") == "true",
                )
            },
            json.optionalObject("wakeEstimate")?.let { estimate ->
                fun time(key: String) = (estimate[key] as? JsonPrimitive)?.takeIf { it.isString }?.content
                SyncWakeEstimate(time("workdays"), time("offDays"))
            },
            readActionResults(json["actionResults"]),
            readEndEarly(json["endEarly"]),
            (json["goalMinutes"] as? JsonPrimitive)?.takeIf { it !is JsonNull }?.intOrNull,
            (json["profileOptions"] as? JsonArray)?.mapNotNull { element ->
                val option = element as? JsonObject ?: return@mapNotNull null
                val id = optionalText(option, "id") ?: return@mapNotNull null
                SyncProfileOption(id, optionalText(option, "name") ?: "")
            },
            optionalText(json, "defaultProfileId"),
            readBeeminder(json["beeminder"]),
            readNoise(json["noise"]),
        )
    }

    // ADR 42, 36: a noise player that can't be read is left out rather than failing the file.
    private fun readNoise(value: JsonElement?): SyncNoise? {
        val noise = value as? JsonObject ?: return null
        return runCatching {
            SyncNoise(
                (noise["playing"] as? JsonPrimitive)?.takeIf { !it.isString }?.booleanOrNull ?: false,
                optionalText(noise, "name"),
                ((noise["volume"] as? JsonPrimitive)?.takeIf { !it.isString }?.doubleOrNull ?: 0.0).coerceIn(0.0, 1.0),
                (noise["locked"] as? JsonPrimitive)?.takeIf { !it.isString }?.booleanOrNull ?: false,
                noise.requiredTime("at"),
            )
        }.getOrNull()
    }

    // ADR 42: an optional list; an entry that can't be read is left out rather than failing the file.
    private fun readActionResults(value: JsonElement?): List<SyncActionResult>? {
        val array = value as? JsonArray ?: return null
        return array.mapNotNull { element ->
            val item = element as? JsonObject ?: return@mapNotNull null
            runCatching {
                SyncActionResult(
                    item.requiredString("id"),
                    (item["ok"] as? JsonPrimitive)?.takeIf { !it.isString }?.booleanOrNull ?: false,
                    // A result without a status (shouldn't happen) reads by its ok flag.
                    optionalText(item, "status") ?: if ((item["ok"] as? JsonPrimitive)?.booleanOrNull == true) SyncActionResult.STATUS_APPLIED else SyncActionResult.STATUS_REFUSED,
                    optionalText(item, "message"),
                    optionalText(item, "planVersion"),
                    item.optionalTime("planEditedAt"),
                )
            }.getOrNull()
        }
    }

    // ADR 42: an offer that can't be read is left out rather than failing the file.
    private fun readEndEarly(value: JsonElement?): SyncEndEarlyOffer? {
        val offer = value as? JsonObject ?: return null
        return runCatching {
            SyncEndEarlyOffer(
                offer.requiredString("requestId"),
                offer.requiredTime("phaseStartedAt"),
                offer.requiredTime("allowedFrom"),
                (offer["minReasonLength"] as? JsonPrimitive)?.intOrNull ?: 0,
                optionalText(offer, "note"),
            )
        }.getOrNull()
    }

    /** An optional string field; anything that isn't a string reads as null. */
    private fun optionalText(json: JsonObject, name: String): String? =
        (json[name] as? JsonPrimitive)?.takeIf { it !is JsonNull && it.isString }?.content

    // ADR 43: an optional list; an entry that can't be read is left out rather than failing the file.
    private fun readBeeminder(value: kotlinx.serialization.json.JsonElement?): List<SyncBeeminderGoal>? {
        val array = value as? JsonArray ?: return null
        return array.mapNotNull { element ->
            val item = element as? JsonObject ?: return@mapNotNull null
            runCatching {
                SyncBeeminderGoal(
                    item.requiredString("title"),
                    (item["needed"] as? JsonPrimitive)?.takeIf { it.isString }?.content ?: "",
                    (item["safeDays"] as? JsonPrimitive)?.content?.toIntOrNull() ?: 0,
                    item.requiredTime("dueAt"),
                )
            }.getOrNull()
        }
    }

    private fun writeSmartLevel(level: SyncSmartLevel): JsonObject = buildJsonObject {
        level.levelMinutes?.let { put("levelMinutes", it) }
        put("nights", level.nights)
        put("latencyMinutes", level.latencyMinutes)
        level.averageAsleep?.let { put("averageAsleep", it) }
        level.averageSleptMinutes?.let { put("averageSleptMinutes", it) }
        put("madeThisWeek", level.madeThisWeek)
        put("askedThisWeek", level.askedThisWeek)
    }

    // ADR 34: a part that can't be read is left out rather than failing the file.
    private fun readSmartLevel(json: JsonObject): SyncSmartLevel? = runCatching {
        fun int(key: String): Int? = (json[key] as? kotlinx.serialization.json.JsonPrimitive)?.content?.toIntOrNull()
        SyncSmartLevel(
            int("levelMinutes"), int("nights") ?: 0, int("latencyMinutes") ?: 15,
            (json["averageAsleep"] as? kotlinx.serialization.json.JsonPrimitive)?.takeIf { it.isString }?.content,
            int("averageSleptMinutes"), int("madeThisWeek") ?: 0, int("askedThisWeek") ?: 0,
        )
    }.getOrNull()

    // ADR 26: an optional list; an entry that can't be read is left out rather than failing the file.
    private fun readActions(value: kotlinx.serialization.json.JsonElement?): List<SyncAction>? {
        val array = value as? JsonArray ?: return null
        return array.mapNotNull { element ->
            val item = element as? JsonObject ?: return@mapNotNull null
            runCatching {
                SyncAction(
                    item.requiredString("id"), item.requiredString("kind"), readDeviceId(item.requiredString("target")),
                    (item["item"] as? kotlinx.serialization.json.JsonPrimitive)?.takeIf { it.isString }?.content,
                    (item["text"] as? kotlinx.serialization.json.JsonPrimitive)?.takeIf { it.isString }?.content,
                    item.requiredTime("at"),
                )
            }.getOrNull()
        }
    }

    // ADR 30: an optional list; an entry that can't be read is left out rather than failing the file.
    private fun readComebacks(value: kotlinx.serialization.json.JsonElement?): List<SyncComeback>? {
        val array = value as? JsonArray ?: return null
        return array.mapNotNull { element ->
            val item = element as? JsonObject ?: return@mapNotNull null
            runCatching { SyncComeback(item.requiredString("id"), item.requiredString("text"), item.requiredTime("createdAt")) }.getOrNull()
        }
    }

    // ADR 26: an optional list; an entry that can't be read is left out rather than failing the file.
    private fun readPickups(value: kotlinx.serialization.json.JsonElement?): List<SyncPickup>? {
        val array = value as? JsonArray ?: return null
        return array.mapNotNull { element ->
            val item = element as? JsonObject ?: return@mapNotNull null
            runCatching {
                SyncPickup(
                    readDeviceId(item.requiredString("deviceId")), item.requiredTime("sessionStart"), item.requiredInt("count"),
                    item.requiredInt("longestDownMinutes"), item.requiredTime("countedUntil"),
                    (item["emergencyMinutes"] as? kotlinx.serialization.json.JsonPrimitive)?.content?.toIntOrNull() ?: 0,
                )
            }.getOrNull()
        }
    }

    private fun readDevice(json: JsonObject): SyncDeviceCard {
        val deviceId = readDeviceId(json.requiredString("deviceId"))
        if (deviceId == emptyDeviceId) {
            throw NotASyncFileException()
        }

        return SyncDeviceCard(
            deviceId,
            json.requiredString("name"),
            json.requiredString("kind"),
            json.requiredString("appVersion"),
            json.requiredTime("writtenAt"),
            json.requiredStrings("methods"),
            json.flag("historyOff"),
            json.requiredObject("seen").entries.associate { (id, seen) -> readDeviceId(id) to readTime(stringOf(seen)) },
            (json["companions"] as? JsonArray).orEmpty().map { element ->
                val companion = element as? JsonObject ?: throw NotASyncFileException()
                SyncCompanion(
                    companion.requiredString("name"),
                    companion.requiredString("kind"),
                    companion.flag("linked"),
                    companion.optionalTime("lastHeard"),
                )
            },
            json.optionalTime("nextAlarm"),
        )
    }

    private fun readSession(json: JsonObject): SyncSession = SyncSession(
        json.requiredTime("start"),
        json.optionalTime("end"),
        json.optionalString("profile"),
        json.flag("added"),
        SessionTaskItem.read(json["tasks"] as? JsonArray),
    )

    private fun readDrift(json: JsonObject): SyncDrift = SyncDrift(
        json.requiredTime("start"),
        json.requiredTime("end"),
        json.requiredString("app"),
        json.optionalString("site"),
        json.optionalString("section"),
    )

    private fun readDayPlan(json: JsonObject): SyncDayPlan = SyncDayPlan(
        json.requiredString("planJson"),
        json.requiredString("version"),
        json.requiredTime("editedAt"),
        json.optionalString("basedOn"),
    )

    private fun readDocument(json: JsonObject): SyncDocument = SyncDocument(
        json.requiredString("key"),
        json.requiredString("json"),
        json.requiredTime("editedAt"),
    )

    // The PC reads a Guid only in its 36-character form (upper or lower case); java.util.UUID would take shorter ones.
    private fun readDeviceId(text: String): UUID =
        if (deviceIdForm.matches(text)) UUID.fromString(text) else throw NotASyncFileException()

    private fun readTime(text: String): Instant = try {
        OffsetDateTime.parse(text).toInstant()
    } catch (e: DateTimeParseException) {
        throw NotASyncFileException()
    }

    /** A JSON string's text; anything else (null, a number, an object) isn't. */
    private fun stringOf(value: JsonElement?): String =
        (value as? JsonPrimitive)?.takeIf { it !is JsonNull && it.isString }?.content ?: throw NotASyncFileException()

    private fun JsonObject.requiredString(name: String): String = stringOf(this[name])

    private fun JsonObject.optionalString(name: String): String? = when (val value = this[name]) {
        null, JsonNull -> null
        else -> stringOf(value)
    }

    private fun JsonObject.requiredTime(name: String): Instant = readTime(requiredString(name))

    private fun JsonObject.optionalTime(name: String): Instant? = optionalString(name)?.let(::readTime)

    // As the PC's reader: a number, or a number in a string.
    private fun JsonObject.requiredInt(name: String): Int =
        (this[name] as? JsonPrimitive)?.takeIf { it !is JsonNull }?.intOrNull ?: throw NotASyncFileException()

    // As the PC's reader: missing is false; null or anything but true and false isn't read.
    private fun JsonObject.flag(name: String): Boolean = when (val value = this[name]) {
        null -> false
        is JsonPrimitive -> value.takeIf { it !is JsonNull && !it.isString }?.booleanOrNull ?: throw NotASyncFileException()
        else -> throw NotASyncFileException()
    }

    private fun JsonObject.requiredObject(name: String): JsonObject = this[name] as? JsonObject ?: throw NotASyncFileException()

    private fun JsonObject.optionalObject(name: String): JsonObject? = when (val value = this[name]) {
        null, JsonNull -> null
        is JsonObject -> value
        else -> throw NotASyncFileException()
    }

    private fun JsonObject.requiredArray(name: String): JsonArray = this[name] as? JsonArray ?: throw NotASyncFileException()

    private fun JsonObject.requiredObjects(name: String): List<JsonObject> =
        requiredArray(name).map { it as? JsonObject ?: throw NotASyncFileException() }

    private fun JsonObject.requiredStrings(name: String): List<String> = requiredArray(name).map(::stringOf)

    /** Thrown inside [read] at the first thing that can't be read; [read] turns it into null. */
    private class NotASyncFileException : Exception()
}
