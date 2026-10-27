package com.accountabilibuddy.companion.data

import com.accountabilibuddy.companion.data.JsonValues.bool
import com.accountabilibuddy.companion.data.JsonValues.int
import com.accountabilibuddy.companion.data.JsonValues.obj
import com.accountabilibuddy.companion.data.JsonValues.requireString
import com.accountabilibuddy.companion.data.JsonValues.string
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayMode
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.model.FocusProfile
import com.accountabilibuddy.companion.model.MatchKind
import com.accountabilibuddy.companion.model.MatchTarget
import com.accountabilibuddy.companion.model.OneOffDay
import com.accountabilibuddy.companion.model.PlanPart
import com.accountabilibuddy.companion.model.PlanTemplate
import com.accountabilibuddy.companion.model.PlanCycle
import com.accountabilibuddy.companion.model.ProfileDocument
import com.accountabilibuddy.companion.model.Rule
import com.accountabilibuddy.companion.model.RuleVerdict
import com.accountabilibuddy.companion.model.UnlockOption
import com.accountabilibuddy.companion.model.WeekdayTemplate
import com.accountabilibuddy.companion.model.WorkWindow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.Duration
import java.util.UUID

/**
 * Reads the Windows app's profile document (ADR 7), like its ProfileDocumentSerializer: a document from before
 * schema 5 gets the starting day plan (its weekly schedule is gone, ADR 18), a newer version is refused, and fields
 * the phone doesn't use are skipped.
 */
object ProfileDocumentParser {
    private const val DAY_PLAN_SCHEMA_VERSION = 5
    private const val COMMS_SCHEMA_VERSION = 6
    private const val PRE_WORK_SCHEMA_VERSION = 7

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
        val read = if (version < DAY_PLAN_SCHEMA_VERSION) DayPlan.Default else root.obj("dayPlan")?.let(::dayPlan) ?: DayPlan.Default
        // v6 (comms windows): a v5 plan gains the Comms category; the PC itself upgrades an untouched starting plan.
        val withComms = if (version < COMMS_SCHEMA_VERSION && read.categories.none { it.behavior == CategoryBehavior.Comms }) read.copy(categories = read.categories + DayPlan.comms) else read
        // v7 (ADR 31): every plan gets the Pre-Work category, with no parts.
        val plan = if (version < PRE_WORK_SCHEMA_VERSION && withComms.categories.none { it.behavior == CategoryBehavior.PreWork }) {
            withComms.copy(categories = withComms.categories + DayPlan.preWork)
        } else {
            withComms
        }
        return ProfileDocument(ProfileDocument.CURRENT_SCHEMA_VERSION, profiles, plan)
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

    /**
     * A day plan on its own, in the document's shape (also a conflict answer's plan, ADR 19). A plan in the v11 shape
     * (a "workday" and an "otherDay" template and "workdays", no "templates") becomes the two named templates and week A
     * of the cycle, like the PC's DayModesMigration, so a plan saved by an earlier version of the app still reads.
     */
    fun dayPlan(json: JsonObject): DayPlan {
        val categories = (json["categories"] as? JsonArray).orEmpty().map {
            val c = it.jsonObject
            DayCategory(
                id = JsonValues.uuid(c.requireString("id")),
                name = c.requireString("name"),
                color = c.requireString("color"),
                behavior = JsonValues.enumValue<CategoryBehavior>(c["behavior"], "category behaviour"),
            )
        }
        val oneOffDays = (json["oneOffDays"] as? JsonArray).orEmpty().map {
            val d = it.jsonObject
            OneOffDay(
                id = JsonValues.uuid(d.requireString("id")),
                date = JsonValues.date(d.requireString("date")),
                template = template(d.obj("template")),
                note = d.string("note"),
            )
        }

        if (json["templates"] == null && json["workday"] != null) {
            val workdays = (json["workdays"] as? JsonArray).orEmpty().map { JsonValues.dayOfWeek(it.jsonPrimitive.content) }.toSet()
            return DayPlan.fromWorkdayAndOffDay(categories, workdays, template(json.obj("workday")), template(json.obj("otherDay")), oneOffDays)
        }

        return DayPlan(
            categories = categories,
            templates = (json["templates"] as? JsonArray).orEmpty().map {
                val t = it.jsonObject
                PlanTemplate(JsonValues.uuid(t.requireString("id")), t.requireString("name"), template(t.obj("template")))
            },
            cycle = cycle(json),
            oneOffDays = oneOffDays,
        )
    }

    /**
     * The plan's repeating cycle (schema v13): "cycle" with its weeks and the Monday week 1 starts on. A plan in the v12
     * shape (a "week" with A, an optional B and the Monday B starts on, no "cycle") becomes a cycle like the PC's
     * DayCycleMigration: A only is a one-week cycle, A and B a two-week cycle that starts a week after B's start, so every
     * date follows the week it did before.
     */
    private fun cycle(json: JsonObject): PlanCycle {
        val cycle = json.obj("cycle")
        if (cycle != null) {
            return PlanCycle(
                weeks = (cycle["weeks"] as? JsonArray).orEmpty().map { weekdays(it) },
                startsOn = JsonValues.date(cycle.requireString("startsOn")),
            )
        }

        val week = json.obj("week")
        val a = weekdays(week?.get("a"))
        val b = (week?.get("b") as? JsonArray)?.let { weekdays(it) }
        val bStartsOn = week?.string("bStartsOn")?.let(JsonValues::date)
        return if (b != null && bStartsOn != null) PlanCycle(listOf(a, b), bStartsOn.plusDays(7))
        else PlanCycle(listOf(a), DayPlan.DEFAULT_CYCLE_START)
    }

    private fun weekdays(json: kotlinx.serialization.json.JsonElement?): List<WeekdayTemplate> = (json as? JsonArray).orEmpty().map {
        val entry = it.jsonObject
        WeekdayTemplate(JsonValues.dayOfWeek(entry.requireString("day")), JsonValues.uuid(entry.requireString("templateId")))
    }

    private fun template(json: JsonObject?): DayTemplate = DayTemplate(
        (json?.get("parts") as? JsonArray).orEmpty().map {
            val p = it.jsonObject
            PlanPart(
                id = JsonValues.uuid(p.requireString("id")),
                categoryId = JsonValues.uuid(p.requireString("categoryId")),
                start = JsonValues.time(p.requireString("start")),
                end = JsonValues.time(p.requireString("end")),
                label = p.string("label"),
                flexMinutes = p.int("flexMinutes"),
                hintAt = p.string("hintAt")?.let(JsonValues::time),
                wakeByAlarm = (p["wakeByAlarm"] as? kotlinx.serialization.json.JsonPrimitive)?.content == "true",
                autoStart = (p["autoStart"] as? kotlinx.serialization.json.JsonPrimitive)?.content == "true",
            )
        },
        blocks = json?.get("blocks")?.let { it as? JsonObject }?.let(BlockRulesJson::read),
        // v12 (ADR 47): optional; a template from before has none, which reads as a plain Schedule day.
        mode = json?.get("mode")?.takeUnless { it is JsonNull }?.let { JsonValues.enumValue<DayMode>(it, "day mode") } ?: DayMode.Schedule,
        workWindow = json?.obj("workWindow")?.let { WorkWindow(JsonValues.time(it.requireString("start")), JsonValues.time(it.requireString("end"))) },
        budgetMinutes = json?.int("budgetMinutes"),
    )
}
