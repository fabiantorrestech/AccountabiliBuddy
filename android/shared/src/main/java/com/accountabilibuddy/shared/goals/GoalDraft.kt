package com.accountabilibuddy.shared.goals

import java.time.LocalDate
import java.util.UUID

/**
 * A goal being made or edited on the phone (round 2 R6b2; the PC's `GoalEditorViewModel` + `GoalTargetEditorViewModel` + the sources
 * editor, as plain data so the rules run without a screen): step 1 is the name, kind, icon, colour and area; step 2 the target and
 * what counts. Numbers are kept as the text typed, so a half-typed value is never lost ([GoalDrafts] reads them). Nothing here is
 * stored; [GoalDrafts.toGoal] makes the goal the store takes.
 *
 * @property id The goal's id (a new goal: made when the sheet opens).
 * @property name The user's private text: never logged.
 * @property amount Per period: minutes (time), times (a weekly or monthly habit), a value (amount). Blank = none.
 * @property unit An amount goal's unit (Count, Steps, Lb or Kg); the other kinds have their own ([GoalDrafts.unitOf]).
 * @property threshold A habit's "ticks itself at N min" of its time sources; blank = ticked by hand only.
 * @property weeklyDays "On N days" of a weekly or monthly amount or time goal; blank = the period's total counts.
 * @property longTerm The long-term target: hours for a time goal, days for a habit, the unit otherwise; blank = none.
 * @property habitUnit A habit keeps the unit it was stored with (a tick, or a count of days hit).
 * @property weeklyHours Deep work only: its weekly target in hours; blank = none.
 * @property sources What counts; every rule keeps its id.
 * @property notes Why it matters, with links: the user's private text, never logged. Blank = none.
 * @property extraTiles The goal's own Streak / Total switches; null = it follows Goals › Options › Extra tiles.
 */
data class GoalDraft(
    val id: UUID,
    val name: String = "",
    val kind: GoalKind = GoalKind.Habit,
    val icon: String = GoalDrafts.DEFAULT_ICON,
    val color: String = GoalDrafts.DEFAULT_COLOR,
    val areaId: UUID? = null,
    val period: GoalPeriod = GoalPeriod.Day,
    val amount: String = "",
    val unit: GoalUnit = GoalUnit.Count,
    val direction: GoalDirection = GoalDirection.AtLeast,
    val threshold: String = "",
    val weeklyDays: String = "",
    val longTerm: String = "",
    val targetDate: LocalDate? = null,
    val habitUnit: GoalUnit = GoalUnit.YesNo,
    val weeklyHours: String = "",
    val sources: List<GoalSourceRule> = emptyList(),
    val isDeepWork: Boolean = false,
    val notes: String = "",
    val extraTiles: GoalExtraTiles? = null,
)
