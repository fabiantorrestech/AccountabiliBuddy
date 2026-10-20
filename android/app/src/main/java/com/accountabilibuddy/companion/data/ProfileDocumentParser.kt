package com.accountabilibuddy.companion.data

import com.accountabilibuddy.companion.data.JsonValues.bool
import com.accountabilibuddy.companion.data.JsonValues.int
import com.accountabilibuddy.companion.data.JsonValues.obj
import com.accountabilibuddy.companion.data.JsonValues.requireString
import com.accountabilibuddy.companion.data.JsonValues.string
import com.accountabilibuddy.companion.model.FocusProfile
import com.accountabilibuddy.companion.model.MatchKind
import com.accountabilibuddy.companion.model.MatchTarget
import com.accountabilibuddy.companion.model.OneOffBlock
import com.accountabilibuddy.companion.model.ProfileDocument
import com.accountabilibuddy.companion.model.Rule
import com.accountabilibuddy.companion.model.RuleVerdict
import com.accountabilibuddy.companion.model.ScheduleBlock
import com.accountabilibuddy.companion.model.TimeOff
import com.accountabilibuddy.companion.model.UnlockOption
import com.accountabilibuddy.companion.model.WeeklySchedule
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.Duration
import java.util.UUID

/**
 * Reads the Windows app's profile document (ADR 7), like its ProfileDocumentSerializer: versions 2 and 3 are
 * migrated (off days become all-day time off), a newer version is refused, and fields the phone doesn't use are
 * skipped.
 */
object ProfileDocumentParser {
    private const val TIME_OFF_SCHEMA_VERSION = 4

    fun parse(json: String): ProfileDocument {
        val root = try {
            Json.parseToJsonElement(json).jsonObject
        } catch (e: Exception) {
            throw ImportException("The profiles aren't valid JSON.", e)
        }

        return parse(root)
    }

    fun parse(root: JsonObject): ProfileDocument {
        val version = root.int("schemaVersion") ?: throw ImportException("The profiles have no schema version.")
        if (version > ProfileDocument.CURRENT_SCHEMA_VERSION) {
            throw ImportException("These profiles come from a newer AccountabiliBuddy (schema $version). Update the phone app first.")
        }

        val profiles = (root["profiles"] as? JsonArray).orEmpty().map { profile(it.jsonObject) }
        val schedule = schedule(root.obj("schedule"), version)
        return ProfileDocument(ProfileDocument.CURRENT_SCHEMA_VERSION, profiles, schedule)
    }

    private fun profile(json: JsonObject): FocusProfile = FocusProfile(
        id = JsonValues.uuid(json.requireString("id")),
        name = json.requireString("name"),
        rules = (json["rules"] as? JsonArray).orEmpty().map { rule(it.jsonObject) },
        unknownGracePeriod = json.string("unknownGracePeriod")?.let(JsonValues::duration) ?: Duration.ofSeconds(30),
        unlockOptions = (json["unlockOptions"] as? JsonArray).orEmpty().map { option ->
            val o = option.jsonObject
            UnlockOption(JsonValues.uuid(o.requireString("id")), o.requireString("name"), JsonValues.duration(o.requireString("wait")))
        },
        askAboutUnlistedSites = json.bool("askAboutUnlistedSites", default = true),
    )

    private fun rule(json: JsonObject): Rule = Rule(
        id = JsonValues.uuid(json.requireString("id")),
        target = JsonValues.enumValue<MatchTarget>(json["target"], "rule target"),
        kind = JsonValues.enumValue<MatchKind>(json["kind"], "match kind"),
        pattern = json.requireString("pattern"),
        verdict = JsonValues.enumValue<RuleVerdict>(json["verdict"], "verdict"),
        note = json.string("note"),
    )

    private fun schedule(json: JsonObject?, version: Int): WeeklySchedule {
        if (json == null) {
            return WeeklySchedule()
        }

        val blocks = (json["blocks"] as? JsonArray).orEmpty().map {
            val b = it.jsonObject
            ScheduleBlock(
                id = JsonValues.uuid(b.requireString("id")),
                day = JsonValues.dayOfWeek(b.requireString("day")),
                start = JsonValues.time(b.requireString("start")),
                end = JsonValues.time(b.requireString("end")),
                profileId = JsonValues.uuid(b.requireString("profileId")),
            )
        }

        val timeOff = if (version < TIME_OFF_SCHEMA_VERSION) {
            // v2/v3: "offDays": ["2026-10-20"] are all-day time off.
            (json["offDays"] as? JsonArray).orEmpty().map { TimeOff(UUID.randomUUID(), JsonValues.date(it.jsonPrimitive.content)) }
        } else {
            (json["timeOff"] as? JsonArray).orEmpty().map {
                val t = it.jsonObject
                TimeOff(
                    id = JsonValues.uuid(t.requireString("id")),
                    date = JsonValues.date(t.requireString("date")),
                    start = t.string("start")?.let(JsonValues::time),
                    end = t.string("end")?.let(JsonValues::time),
                    note = t.string("note"),
                )
            }
        }

        val oneOff = (json["oneOffBlocks"] as? JsonArray).orEmpty().map {
            val o = it.jsonObject
            OneOffBlock(
                id = JsonValues.uuid(o.requireString("id")),
                date = JsonValues.date(o.requireString("date")),
                start = JsonValues.time(o.requireString("start")),
                end = JsonValues.time(o.requireString("end")),
                profileId = JsonValues.uuid(o.requireString("profileId")),
            )
        }

        return WeeklySchedule(blocks, timeOff, oneOff)
    }
}
