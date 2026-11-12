package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalDefaults
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalListRules
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalPauseStretch
import com.accountabilibuddy.shared.goals.GoalPendingChange
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalReminder
import com.accountabilibuddy.shared.goals.GoalReminderDays
import com.accountabilibuddy.shared.goals.GoalReviewRecord
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalSources
import com.accountabilibuddy.shared.goals.GoalTarget
import com.accountabilibuddy.shared.goals.GoalUnit
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID

/**
 * Round 2 R6a: the phone goals store's pure rules ([GoalListRules]), checked on their own (no fixture: their C# twins are the PC's
 * `GoalStore` / `GoalsService`, which run on a database). Each line names the PC rule it follows: the hand-entry stamp (Q423), the
 * tidy-up after a change (Deep work kept whole, a goal with no live area to "Work", tombstones kept 30 days), the first run's items,
 * and taking a backup's goals in (Q387, Q421: restore vs merge, deletions ignored, Deep work cosmetic only, no second built-in goal,
 * sources allowed now only, no waiting change or second target, no entry for Deep work or a dead goal, notes only with a password).
 */
object GoalListRulesCheck {
    private val offset = ZoneOffset.ofHours(-5)
    private val now: OffsetDateTime = OffsetDateTime.of(2026, 11, 11, 18, 0, 0, 0, offset)
    private val today: LocalDate = LocalDate.of(2026, 11, 11)

    private fun id(n: Int): UUID = UUID.fromString("00000000-0000-0000-0000-%012d".format(n))

    private fun at(hours: Long): OffsetDateTime = now.plusHours(hours)

    private fun area(n: Int, edited: OffsetDateTime = at(-48), deletedAt: OffsetDateTime? = null) =
        GoalArea(id(n), "Area $n", "school", "#2E7D6B", n.toDouble(), edited, deletedAt)

    private fun goal(n: Int, areaId: UUID, edited: OffsetDateTime = at(-48), notes: String? = null, sources: List<GoalSourceRule> = listOf(hand()), builtIn: Boolean = false) =
        Goal(id(n), areaId, "Goal $n", "school", "#2E7D6B", GoalKind.Habit, GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo), sources, at(-24 * 30), n.toDouble(), edited, builtIn = builtIn, notes = notes)

    private fun hand() = GoalSourceRule(id(900), GoalSourceType.Hand)

    private fun entry(n: Int, goalId: UUID, edited: OffsetDateTime, source: GoalEntrySource = GoalEntrySource.Hand, day: LocalDate = today) =
        GoalEntry(id(n), goalId, day, null, 1.0, source, edited)

    fun run(report: CheckReport) {
        val name = "goals-store"
        // Q423: the hand-entry stamp.
        val g = id(500)
        report.check("$name: a first hand entry is stamped now", GoalListRules.handEntryStamp(emptyList(), g, today, now) == now)
        val same = listOf(entry(1, g, now))
        report.check("$name: Q423 a hand entry in the same clock reading is one tick (100 ns) later", GoalListRules.handEntryStamp(same, g, today, now) == now.plusNanos(100))
        val ahead = listOf(entry(1, g, now.plusSeconds(5)))
        report.check("$name: Q423 a clock behind the newest hand entry still stamps after it", GoalListRules.handEntryStamp(ahead, g, today, now) == now.plusSeconds(5).plusNanos(100))
        val others = listOf(entry(1, g, now, day = today.minusDays(1)), entry(2, id(501), now), entry(3, g, now, GoalEntrySource.HealthConnect))
        report.check("$name: Q423 only that goal's hand entries of that day count", GoalListRules.handEntryStamp(others, g, today, now) == now)

        // The first run's items and the tidy-up.
        val work = GoalDefaults.newWorkArea(now)
        val deep = GoalDefaults.newDeepWork(work.id, now)
        report.check(
            "$name: first run = Work (fixed id) + Deep work (fixed id, built in, all focus only, Show on Today, no stored target)",
            work.id == UUID.fromString("d33b0a11-6f0c-4e2a-9d51-0000000000a1") && deep.id == Goal.DEEP_WORK_ID && deep.builtIn && deep.areaId == work.id &&
                deep.sources == listOf(GoalSources.DEEP_WORK_RULE) && deep.showOnToday && deep.target.amount == null && deep.secondTarget == null,
        )
        val messy = deep.copy(
            kind = GoalKind.Habit, builtIn = false, secondTarget = GoalTarget(GoalPeriod.Week, 600.0, GoalUnit.Minutes),
            sources = listOf(hand()), pauses = listOf(GoalPauseStretch(today)), target = GoalTarget(GoalPeriod.Day, 30.0, GoalUnit.Minutes),
        )
        val tidied = GoalListRules.tidy(GoalLists(listOf(work), listOf(messy)), now).goals.single()
        report.check(
            "$name: tidy keeps Deep work whole (Time, built in, all focus only, no pause, no stored target or second target)",
            tidied.kind == GoalKind.Time && tidied.builtIn && tidied.sources == listOf(GoalSources.DEEP_WORK_RULE) && tidied.pauses == null &&
                tidied.target.amount == null && tidied.secondTarget == null && tidied.name == messy.name,
        )
        val gone = area(2, deletedAt = at(-1))
        val orphan = goal(501, gone.id)
        val moved = GoalListRules.tidy(GoalLists(listOf(work, gone), listOf(orphan)), now).goals.single()
        report.check("$name: tidy sends a live goal whose area is gone to Work", moved.areaId == work.id)
        val old = area(3, deletedAt = now.minusDays(31))
        val recent = area(4, deletedAt = now.minusDays(29))
        val kept = GoalListRules.tidy(GoalLists(listOf(work, old, recent)), now).areas.map { it.id }
        report.check("$name: tidy drops tombstones older than 30 days, keeps newer ones", id(3) !in kept && id(4) in kept)

        // GS0: the PC's Normalize rule (GoalAreaTidy, stamps from the lists, never the clock), Deep work first, no other built-in goal,
        // and what this version doesn't know kept from the lists before the change (ADR 79).
        val other = area(5)
        val movedAgain = GoalListRules.tidy(GoalLists(listOf(other, gone), listOf(orphan)), now)
        val newWork = movedAgain.areas.firstOrNull { it.id == GoalDefaults.WORK_AREA_ID }
        report.check(
            "$name: GS0 with Work gone, a goal in a dead area goes to a new Work (not the first live area), placed last, stamped one tick after the deletion",
            movedAgain.goals.single().areaId == GoalDefaults.WORK_AREA_ID && movedAgain.goals.single().editedAt == at(-1).plusNanos(100) &&
                newWork != null && newWork.isLive && newWork.position == 6.0 && newWork.editedAt == at(-1).plusNanos(100),
        )
        report.check("$name: GS0 the move doesn't depend on the clock", GoalListRules.tidy(GoalLists(listOf(other, gone), listOf(orphan)), now.plusDays(3)).goals == movedAgain.goals)
        val ordered = GoalListRules.tidy(GoalLists(listOf(work), listOf(goal(510, work.id, builtIn = true), deep)), now)
        report.check("$name: GS0 Deep work first and no other goal built in (the PC's Normalize)", ordered.goals.map { it.id } == listOf(Goal.DEEP_WORK_ID, id(510)) && !ordered.goals[1].builtIn)
        val odd = kotlinx.serialization.json.buildJsonObject { put("future", kotlinx.serialization.json.JsonPrimitive(1)) }
        val stored = GoalLists(listOf(work), listOf(deep.copy(target = deep.target.copy(extra = odd), extra = odd), goal(511, work.id).copy(extra = odd)))
        val rebuilt = GoalListRules.tidy(GoalLists(listOf(work), listOf(deep, goal(511, work.id).copy(name = "Renamed"))), now, before = stored)
        report.check(
            "$name: GS0 tidy puts back unknown fields a change left out (Deep work's target too, which normalizing replaces)",
            rebuilt.goals.all { it.extra == odd } && rebuilt.goals.first().target.extra == odd && rebuilt.goals[1].name == "Renamed",
        )

        // Taking a backup in.
        val local = GoalLists(
            areas = listOf(work, area(2)),
            goals = listOf(deep.copy(editedAt = at(-48)), goal(501, id(2), edited = at(-1), notes = "mine"), goal(502, id(2), edited = at(-10), notes = "mine too")),
            reminders = listOf(GoalReminder(id(700), id(501), GoalReminderDays.EveryDay, LocalTime.of(20, 0), 0.0, at(-10), reason = "my reason")),
            reviews = listOf(GoalReviewRecord(id(800), today.minusDays(3), emptyList(), 0.0, at(-10), notes = "my review")),
        )
        val backup = GoalLists(
            areas = listOf(area(2, edited = at(-5)).copy(name = "From backup"), area(5, edited = at(-5))),
            goals = listOf(
                // Older than this phone's: merge keeps this phone's, restore takes it.
                goal(501, id(2), edited = at(-5), notes = "backup notes").copy(name = "Backup 501"),
                // Newer than this phone's: merge takes it (notes stay this phone's without a password).
                goal(502, id(2), edited = at(-2), notes = "backup notes").copy(name = "Backup 502"),
                // A new goal with a waiting change, a second target and sources not allowed (all focus, a task goal link).
                goal(503, id(5), edited = at(-5), sources = listOf(hand(), GoalSourceRule(id(901), GoalSourceType.FocusAll), GoalSourceRule(id(902), GoalSourceType.GoalField))).copy(
                    target = GoalTarget(GoalPeriod.Day, 1.0, GoalUnit.YesNo, pendingChange = GoalPendingChange(0.5, at(1))),
                    secondTarget = GoalTarget(GoalPeriod.Week, 3.0, GoalUnit.Count),
                ),
                // A second built-in goal, and a tombstone that must not delete anything.
                goal(504, id(5), edited = at(-5), builtIn = true),
                goal(505, id(5), edited = at(-5)).copy(deletedAt = at(-5)),
                // Deep work from the backup: name, icon, colour and notes only; its area and targets stay this phone's.
                deep.copy(name = "Deep focus", icon = "timer", areaId = id(5), editedAt = at(-1), notes = "dw notes", showOnToday = false),
            ),
            entries = listOf(entry(600, Goal.DEEP_WORK_ID, at(-5)), entry(601, id(503), at(-5)), entry(602, id(999), at(-5))),
            reminders = listOf(GoalReminder(id(700), id(501), GoalReminderDays.EveryDay, LocalTime.of(21, 0), 0.0, at(-5), reason = "backup reason")),
            reviews = listOf(GoalReviewRecord(id(800), today.minusDays(3), emptyList(), 0.0, at(-5), notes = "backup review")),
        )

        val merged = GoalListRules.tidy(GoalListRules.imported(local, backup, replace = false, includesNotes = false, now = now), now)
        fun Goal?.named() = this?.name
        val m501 = merged.goals.firstOrNull { it.id == id(501) }
        val m502 = merged.goals.firstOrNull { it.id == id(502) }
        val m503 = merged.goals.firstOrNull { it.id == id(503) }
        val mDeep = merged.goals.first { it.id == Goal.DEEP_WORK_ID }
        report.check("$name: merge keeps this phone's newer edit", m501.named() == "Goal 501")
        report.check("$name: merge takes the backup's newer edit, keeping this phone's notes without a password", m502.named() == "Backup 502" && m502?.notes == "mine too")
        report.check("$name: merge never takes the backup's tombstones and no second built-in goal", merged.goals.none { it.id == id(505) || it.id == id(504) })
        report.check(
            "$name: a new goal comes without a waiting change or second target, with only the sources allowed now",
            m503 != null && m503.target.pendingChange == null && m503.secondTarget == null && m503.sources.map { it.type } == listOf(GoalSourceType.Hand),
        )
        report.check(
            "$name: Deep work takes name, icon, show on Today; keeps its area, its locked source and no target; notes stay without a password",
            mDeep.name == "Deep focus" && mDeep.icon == "timer" && !mDeep.showOnToday && mDeep.areaId == work.id && mDeep.sources == listOf(GoalSources.DEEP_WORK_RULE) &&
                mDeep.target.amount == null && mDeep.notes == null,
        )
        report.check(
            "$name: entries come in only for live goals, never for Deep work",
            merged.entries.map { it.id } == listOf(id(601)),
        )
        report.check("$name: merge takes a newer reminder and review but keeps this phone's reason and notes without a password",
            merged.reminders.single().reason == "my reason" && merged.reminders.single().time == LocalTime.of(21, 0) && merged.reviews.single().notes == "my review")
        report.check("$name: the restore count is the backup's live goals but built-in ones", GoalListRules.importedCount(backup) == 3)

        val restored = GoalListRules.tidy(GoalListRules.imported(local, backup, replace = true, includesNotes = true, now = now), now)
        val r501 = restored.goals.first { it.id == id(501) }
        report.check("$name: restore takes the backup's live items stamped now, with notes when the backup has a password", r501.name == "Backup 501" && r501.editedAt == now && r501.notes == "backup notes")
        report.check("$name: restore takes reminder reasons and review notes from a password backup",
            restored.reminders.single().reason == "backup reason" && restored.reviews.single().notes == "backup review")
        report.check("$name: restore keeps this phone's items the backup doesn't have", restored.areas.any { it.id == work.id } && restored.goals.any { it.id == Goal.DEEP_WORK_ID })
        report.check("$name: Deep work from a password backup brings its notes, stamped now", restored.goals.first { it.isDeepWork }.let { it.notes == "dw notes" && it.editedAt == now })
        val tie = GoalListRules.imported(local, GoalLists(goals = listOf(goal(501, id(2), edited = at(-1)).copy(name = "Tie"))), replace = false, includesNotes = false, now = now)
        report.check("$name: an equal edit time keeps this phone's item", tie.goals.first { it.id == id(501) }.name == "Goal 501")
    }
}
