package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeekly
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyResult
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyRules
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalPendingChange
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.jsonPrimitive
import java.time.Duration
import java.util.UUID

/**
 * Round 2 R6b2: the phone's looser-change rule for Deep work's weekly target ([GoalDeepWorkWeeklyRules]) against the PC's own fixtures
 * of `GoalLowering` (`goals/lowering.json`: decide; `goals/lowering-apply.json`: canApply). Every Deep work case there is run through the
 * phone's `set` / `settle` and must end the way the PC's rule does: the same outcome (nothing, at once, waits), the same time the wait
 * ends and mark of a wait made in ProductaBuddy mode; during a session outside ProductaBuddy mode a wait is refused instead. Cases that are not Deep work's, or whose
 * amounts are outside a weekly target's range (30 min to 56h), are skipped and not counted as passes.
 */
class GoalWeeklyParityCheck(override val name: String) : FixtureCheck {
    override val area = "goals"
    // Returned by a case that does not apply to the phone (not Deep work, or out of the weekly range): not a pass, not counted.
    private val SKIPPED = "skipped"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            val label = "$area/$name (phone weekly target): ${case.name}"
            val result = runCatching { if (name == "lowering") lowering(case) else apply(case) }
            val detail = result.getOrElse { "threw ${it.message}" }
            if (detail === SKIPPED) continue
            report.check(label, result.isSuccess && detail == null, detail ?: "")
        }
        if (name == "lowering") wiring(report)
    }

    private fun lowering(case: FixtureCase): String? {
        val input = case.input
        val isDeepWork = input.getValue("builtIn").jsonPrimitive.boolean || UUID.fromString(case.text("goalId")) == Goal.DEEP_WORK_ID
        if (!isDeepWork) return SKIPPED
        fun minutes(key: String): Int? = input.getValue(key).takeIf { it !is JsonNull }?.jsonPrimitive?.doubleOrNull?.toInt()
        val from = minutes("from")
        val to = minutes("to")
        if ((from != null && !GoalDeepWorkWeeklyRules.isValid(from)) || (to != null && !GoalDeepWorkWeeklyRules.isValid(to))) return SKIPPED
        val now = GoalJson.parseTime(case.text("now"))
        val delay = Duration.ofSeconds((input.getValue("delayMinutes").jsonPrimitive.doubleOrNull!! * 60).toLong())
        val pb = input.getValue("productaBuddy").jsonPrimitive.boolean
        val lowerWaits = input.getValue("lowerWaitsNow").jsonPrimitive.boolean
        val outcome = case.expected.let { (it as kotlinx.serialization.json.JsonObject).getValue("outcome").jsonPrimitive.content }
        val state = GoalDeepWorkWeekly(from)

        val quiet = GoalDeepWorkWeeklyRules.set(state, to, pb, sessionRunning = false, lowerWaitsNow = lowerWaits, now = now, looserDelay = delay)
        when (outcome) {
            "Unchanged" -> if (quiet.result != GoalDeepWorkWeeklyResult.Unchanged) return "expected Unchanged, got ${quiet.result}"
            "ApplyNow" -> if (quiet.result != GoalDeepWorkWeeklyResult.Applied || quiet.state.minutes != to) return "expected applied at once to $to, got ${quiet.result} ${quiet.state}"
            "Wait" -> {
                val expected = case.expected as kotlinx.serialization.json.JsonObject
                val at = GoalJson.parseTime(expected.getValue("appliesAt").jsonPrimitive.content)
                val pending = quiet.state.pending
                if (quiet.result != GoalDeepWorkWeeklyResult.Waiting || pending == null) return "expected Wait, got ${quiet.result}"
                if (!pending.appliesAt.toInstant().equals(at.toInstant())) return "wait ends ${pending.appliesAt}, the PC's ${at}"
                if (pending.madeInProductaBuddy != expected.getValue("madeInProductaBuddy").jsonPrimitive.boolean) return "the PB mark differs from the PC's"
                if (pending.amount != to?.toDouble()) return "the waiting amount differs"
                if (quiet.state.minutes != from) return "a waiting change must leave the target as it is"
            }
            else -> return "unknown outcome $outcome"
        }

        // The same ask during a session: a wait is refused outside ProductaBuddy mode (nothing kept); everything else is as without one.
        val inSession = GoalDeepWorkWeeklyRules.set(state, to, pb, sessionRunning = true, lowerWaitsNow = lowerWaits, now = now, looserDelay = delay)
        if (outcome == "Wait" && !pb) {
            if (inSession.result != GoalDeepWorkWeeklyResult.RefusedDuringSession || inSession.state != state) return "a lowering in a session must be refused and keep nothing, got ${inSession.result}"
        } else if (inSession != quiet) {
            return "a session must change nothing here (it only refuses a wait outside ProductaBuddy mode)"
        }

        return null
    }

    // What the app's wiring relies on (CompanionApp reads the trusted clock once and passes it in): the rule takes the instant it is given
    // and reads no clock of its own, so moving the phone's clock changes nothing; a settled wait is decided by the passed instant alone.
    private fun wiring(report: CheckReport) {
        val label = "$area/$name (phone weekly target)"
        val t0 = GoalJson.parseTime("2026-11-12T10:00:00-06:00")
        val delay = Duration.ofMinutes(10)
        val start = GoalDeepWorkWeekly(600)
        val waiting = GoalDeepWorkWeeklyRules.set(start, 300, false, false, false, t0, delay)
        val again = GoalDeepWorkWeeklyRules.set(start, 300, false, false, false, t0, delay)
        report.check("$label: the wait is decided by the passed instant only (same instant, same wait)", waiting == again && waiting.result == GoalDeepWorkWeeklyResult.Waiting)
        val pending = waiting.state
        val early = GoalDeepWorkWeeklyRules.settle(pending, false, false, false, t0.plusMinutes(9), delay)
        val due = GoalDeepWorkWeeklyRules.settle(pending, false, false, false, t0.plusMinutes(10), delay)
        report.check("$label: a passed instant before the wait ends changes nothing, at its end applies", early.minutes == 600 && early.pending != null && due.minutes == 300 && due.pending == null)
        val running = GoalDeepWorkWeeklyRules.settle(pending, false, true, false, t0.plusDays(1), delay)
        report.check("$label: a running session holds a due wait", running.minutes == 600 && running.pending != null)
        val typedBack = GoalDeepWorkWeeklyRules.set(pending, 600, false, false, false, t0.plusMinutes(1), delay)
        report.check("$label: typing the current target back drops the waiting lowering", typedBack.result == GoalDeepWorkWeeklyResult.Unchanged && typedBack.state == start)
    }

    private fun apply(case: FixtureCase): String? {
        fun flag(key: String) = case.input.getValue(key).jsonPrimitive.boolean
        val now = GoalJson.parseTime(case.text("now"))
        val pending = GoalPendingChange(90.0, GoalJson.parseTime(case.text("appliesAt")), flag("madeInProductaBuddy"))
        val settled = GoalDeepWorkWeeklyRules.settle(
            GoalDeepWorkWeekly(120, pending), flag("productaBuddy"), flag("sessionRunning"), flag("lowerWaitsNow"), now, Duration.ofMinutes(10),
        )
        val applied = settled.minutes == 90 && settled.pending == null
        val expected = (case.expected as kotlinx.serialization.json.JsonPrimitive).boolean
        if (applied != expected) return "applied=$applied, the PC's canApply says $expected"
        if (!applied && (settled.minutes != 120 || settled.pending == null)) return "a wait that doesn't apply must stay as it is"
        return null
    }
}
