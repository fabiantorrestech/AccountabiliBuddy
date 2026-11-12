package com.accountabilibuddy.shared.goals

import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID
import kotlin.math.abs
import kotlin.math.roundToInt

/**
 * The rules of the phone's New goal / Edit goal sheets over a [GoalDraft], rule for rule the PC's editor view models
 * (`GoalEditorViewModel.BasicsProblem`, `GoalTargetEditorViewModel.FindProblem` / `BuildTarget` / `SetKind`, `GoalSourceRowViewModel.Missing` /
 * `ToRule`): what step 1 and step 2 need before Next and Create, and the goal that comes out. Raising or lowering an ordinary goal's
 * target applies at once (it feeds no guard); only Deep work's weekly target waits ([GoalDeepWorkWeeklyRules]), and its daily target is
 * the PC's, read only here (ADR 29, [GoalPhoneRules.canEditDailyTarget]).
 */
object GoalDrafts {
    const val MAX_NAME_LENGTH = 80
    const val DEFAULT_ICON = "track_changes"

    /** The PC's `TaskCategory.Palette[5]`: where a new goal's colour starts. */
    const val DEFAULT_COLOR = "#0D9488"

    /** The kinds, in the order the sheet offers them. */
    val KINDS: List<GoalKind> = listOf(GoalKind.Habit, GoalKind.Amount, GoalKind.Time, GoalKind.Milestone)

    /** How often, in the order the sheet offers them (a stored "every 2 weeks" goal keeps its period). */
    val PERIODS: List<GoalPeriod> = listOf(GoalPeriod.Day, GoalPeriod.Week, GoalPeriod.Month)

    /** An amount goal's units. */
    val AMOUNT_UNITS: List<GoalUnit> = listOf(GoalUnit.Count, GoalUnit.Steps, GoalUnit.Lb, GoalUnit.Kg)

    /** The 24 colours of the PC's editor (the phone's list is the same). */
    val COLORS: List<String> = listOf(
        "#2563EB", "#16A34A", "#DB2777", "#EA580C", "#7C3AED", "#0D9488", "#CA8A04", "#DC2626",
        "#0EA5E9", "#06B6D4", "#22C55E", "#84CC16", "#EAB308", "#F97316", "#EF4444", "#F43F5E",
        "#EC4899", "#D946EF", "#A855F7", "#6366F1", "#14B8A6", "#92400E", "#64748B", "#334155",
    )

    /** The icon names the sheets offer: the PC's stored names (each has an icon on both platforms). */
    val ICONS: List<String> = listOf(
        "menu_book", "directions_run", "monitor_weight", "footprint", "timer", "flag",
        "fitness_center", "bedtime", "code", "work", "water_drop", "translate",
        "palette", "self_improvement", "savings", "school", "music_note", "home",
        "center_focus_strong", "track_changes",
    )

    /** A new goal's draft: a daily habit ticked by hand, in [areaId]. */
    fun newDraft(id: UUID, areaId: UUID?): GoalDraft =
        GoalDraft(id = id, areaId = areaId, sources = listOf(GoalSourceRule(UUID.randomUUID(), GoalSourceType.Hand)))

    /** [goal]'s draft; [weeklyMinutes] is Deep work's weekly target (null = none). */
    fun fromGoal(goal: Goal, weeklyMinutes: Int? = null): GoalDraft {
        val target = goal.target
        val threshold = GoalTexts.threshold(goal)
        val longTerm = target.targetAmount?.let { if (goal.kind == GoalKind.Time) it / 60 else it }
        return GoalDraft(
            id = goal.id,
            name = goal.name,
            kind = goal.kind,
            icon = goal.icon,
            color = goal.color,
            areaId = goal.areaId,
            period = target.period,
            amount = target.amount?.let(::numberText).orEmpty(),
            unit = if (target.unit in AMOUNT_UNITS) target.unit else GoalUnit.Count,
            direction = target.direction,
            threshold = threshold?.let(::numberText).orEmpty(),
            weeklyDays = target.weeklyDays?.toString().orEmpty(),
            longTerm = longTerm?.let(::numberText).orEmpty(),
            targetDate = target.targetDate,
            habitUnit = if (goal.kind == GoalKind.Habit && (target.unit == GoalUnit.YesNo || target.unit == GoalUnit.Count)) target.unit else GoalUnit.YesNo,
            weeklyHours = weeklyMinutes?.let { numberText(it / 60.0) }.orEmpty(),
            sources = goal.sources,
            isDeepWork = goal.isDeepWork,
            notes = goal.notes.orEmpty(),
            extraTiles = goal.extraTiles,
        )
    }

    /** The longest notes a goal keeps (the PC's notes box has no limit; the phone's sheet keeps it readable). */
    const val MAX_NOTES_LENGTH = 2000

    /** The notes as the goal stores them: trimmed, cut at [MAX_NOTES_LENGTH], null when blank. */
    fun notesOf(draft: GoalDraft): String? = draft.notes.trim().take(MAX_NOTES_LENGTH).ifEmpty { null }

    /**
     * The draft with the goal's own Streak or Total switch set (brief §4.3: "the per-goal switch in Edit goal overrides it"). The
     * goal starts from what shows now ([shownNow]: its own choice, else Goals › Options), so flipping one switch keeps the other.
     */
    fun withExtraTile(draft: GoalDraft, shownNow: GoalExtraTiles, streak: Boolean? = null, total: Boolean? = null): GoalDraft {
        val base = draft.extraTiles ?: shownNow
        return draft.copy(extraTiles = GoalExtraTiles(streak = streak ?: base.streak, total = total ?: base.total))
    }

    /** The kind changed on step 1: a habit that isn't daily starts at 3 times a period (the PC's `SetKind`). */
    fun withKind(draft: GoalDraft, kind: GoalKind): GoalDraft {
        if (draft.kind == kind || draft.isDeepWork) {
            return draft
        }

        val amount = if (kind == GoalKind.Habit && draft.amount.isBlank() && draft.period != GoalPeriod.Day) "3" else draft.amount
        return draft.copy(kind = kind, amount = amount)
    }

    /** The unit the goal counts in: minutes for time, the stored one for a habit, a count for a milestone, the picked one for an amount. */
    fun unitOf(draft: GoalDraft): GoalUnit = when (draft.kind) {
        GoalKind.Time -> GoalUnit.Minutes
        GoalKind.Habit -> draft.habitUnit
        GoalKind.Milestone -> GoalUnit.Count
        GoalKind.Amount -> draft.unit
    }

    /** At least / at most / reach is asked for time and amount goals only. */
    fun directionOf(draft: GoalDraft): GoalDirection =
        if (showsAmount(draft)) draft.direction else GoalDirection.AtLeast

    fun isHabit(draft: GoalDraft): Boolean = draft.kind == GoalKind.Habit && !draft.isDeepWork

    fun showsTimesPerPeriod(draft: GoalDraft): Boolean = isHabit(draft) && draft.period != GoalPeriod.Day

    fun showsAmount(draft: GoalDraft): Boolean = !draft.isDeepWork && (draft.kind == GoalKind.Time || draft.kind == GoalKind.Amount)

    fun showsWeeklyDays(draft: GoalDraft): Boolean = showsAmount(draft) && draft.period != GoalPeriod.Day

    // ---------------------------------------------------------------- step 1

    /** Why step 1 can't go on (the first thing to fix), or null. */
    fun basicsProblem(draft: GoalDraft): String? {
        val name = draft.name.trim()
        if (name.isEmpty()) return "Give the goal a name."
        if (name.length > MAX_NAME_LENGTH) return "Keep the name under $MAX_NAME_LENGTH characters."
        return if (draft.areaId == null) "Pick an area (or make one)." else null
    }

    // ---------------------------------------------------------------- step 2

    /** Why the target can't be saved yet (the first thing to fix), or null. Deep work: its weekly target only. */
    fun targetProblem(draft: GoalDraft): String? {
        if (draft.isDeepWork) {
            if (isBad(draft.weeklyHours, number(draft.weeklyHours))) return "Type the weekly target as a number of hours."
            val minutes = deepWorkWeeklyMinutes(draft)
            if (minutes != null && !GoalDeepWorkWeeklyRules.isValid(minutes)) return "The weekly target is 30 min to 56h."
            return null
        }

        val amount = number(draft.amount)
        val longTerm = number(draft.longTerm)
        val days = number(draft.weeklyDays)
        val threshold = number(draft.threshold)
        if (isBad(draft.amount, amount) || isBad(draft.longTerm, longTerm) || isBad(draft.weeklyDays, days) || isBad(draft.threshold, threshold)) {
            return "Numbers only, please."
        }

        if (showsTimesPerPeriod(draft) && (amount == null || amount < 1 || amount > 31)) return "How many times? 1 to 31."
        if (showsAmount(draft) && amount != null && amount <= 0) return "The amount is more than 0."
        if (showsAmount(draft) && amount == null && longTerm == null) return "Set an amount for each period, a long-term target, or both."
        if (showsWeeklyDays(draft) && days != null && (days < 1 || days > maxDays(draft.period))) return "The number of days doesn't fit the period."
        if (longTerm != null && longTerm <= 0) return "The long-term target is more than 0."
        if (draft.targetDate != null && longTerm == null) return "A date needs a long-term amount to reach."
        if (isHabit(draft) && threshold != null && (threshold < 1 || threshold > 1440)) return "Ticks itself at 1 to 1,440 min."
        return null
    }

    /** The first source still missing a choice ("Pick a label…"), or null; a source this goal can't take yet is a problem too. */
    fun sourcesProblem(draft: GoalDraft): String? {
        if (draft.isDeepWork) return null
        val shell = shell(draft)
        for (rule in draft.sources) {
            if (rule.type == GoalSourceType.FocusLabel && rule.labelId == null) return "Pick a label for the focus source."
            if (!GoalSources.allowedFor(shell, rule.type)) return "One of its sources can't count on a goal yet."
        }

        return null
    }

    /** Whether Create is open: step 1 valid, and step 2's target and sources too. */
    fun canCreate(draft: GoalDraft): Boolean = basicsProblem(draft) == null && targetProblem(draft) == null && sourcesProblem(draft) == null

    /** The target to save, as the PC's `BuildTarget` builds it; null when [targetProblem] says why not. Deep work's isn't built here. */
    fun buildTarget(draft: GoalDraft): GoalTarget? {
        if (draft.isDeepWork || targetProblem(draft) != null) return null
        val typed = number(draft.amount)
        val amount = if (draft.kind == GoalKind.Habit) (if (draft.period == GoalPeriod.Day) null else typed ?: 1.0) else typed
        val longTerm = number(draft.longTerm)?.let { if (draft.kind == GoalKind.Time) it * 60 else it }
        val days = if (showsWeeklyDays(draft)) number(draft.weeklyDays)?.roundToInt() else null
        return GoalTarget(
            draft.period, amount, unitOf(draft), directionOf(draft), longTerm,
            if (longTerm != null) draft.targetDate else null, days,
        )
    }

    /** A habit's "ticks itself at" minutes (null = by hand only). */
    fun threshold(draft: GoalDraft): Double? = if (isHabit(draft)) number(draft.threshold)?.takeIf { it > 0 } else null

    /** The rules to save: a habit's threshold on its time sources (the PC's `ToRule`). Deep work's are its own. */
    fun buildSources(draft: GoalDraft): List<GoalSourceRule> {
        if (draft.isDeepWork) return draft.sources
        val threshold = threshold(draft)
        return draft.sources.map { it.copy(thresholdMinutes = if (it.countsTime) threshold else null) }
    }

    /**
     * The draft with the weekly review's "Adjust the target" numbers filled in (brief §5.8, the PC's `ApplySuggestion`): the long-term
     * date, the amount per period, or "on N days". Pre-filled only: nothing is stored until Save. Deep work's daily target is the PC's
     * (ADR 29), so its draft is left as it is.
     */
    fun withAdjustment(draft: GoalDraft, adjustment: GoalReviewAdjustment?): GoalDraft {
        if (adjustment == null || draft.isDeepWork) return draft
        var result = draft
        adjustment.targetDate?.let { result = result.copy(targetDate = it) }
        adjustment.amount?.let { result = result.copy(amount = numberText(it)) }
        adjustment.weeklyDays?.let { days ->
            result = if (draft.kind == GoalKind.Habit) result.copy(amount = days.toString()) else result.copy(weeklyDays = days.toString())
        }
        return result
    }

    /** Deep work's weekly target in minutes (null = none). */
    fun deepWorkWeeklyMinutes(draft: GoalDraft): Int? = number(draft.weeklyHours)?.takeIf { it > 0 }?.let { (it * 60).roundToInt() }

    /**
     * Whether saving Edit goal asks Deep work's weekly rule for [wanted] (R6 milestone carry, f81bb32b item 2): a different amount, or
     * the current amount typed back while a lowering waits (the rule then drops the wait, the PC's "Unchanged"). Saving any other edit
     * of Deep work, with the weekly field never touched, leaves a waiting lowering alone.
     */
    fun weeklyChangeAsked(wanted: Int?, current: Int?, waiting: Boolean, fieldEdited: Boolean): Boolean =
        wanted != current || (waiting && fieldEdited)

    /**
     * The goal this draft makes: a new goal ([base] null) or [base] with the draft's changes; null when step 1 or the target isn't
     * valid. Names are trimmed; a new goal is created [now] at the end of its area ([position]); an edit keeps its pauses, age and
     * place unless the area changed. Deep work takes only what the user may change on it (name, icon, colour, area, place): the
     * store keeps the rest, and its weekly target goes through [GoalDeepWorkWeeklyRules].
     */
    fun toGoal(draft: GoalDraft, base: Goal?, now: OffsetDateTime, position: Double): Goal? {
        val area = draft.areaId ?: return null
        if (basicsProblem(draft) != null) return null
        if (base != null && base.isDeepWork) {
            return base.copy(
                name = draft.name.trim(), icon = draft.icon, color = draft.color, areaId = area,
                position = if (area == base.areaId) base.position else position,
                notes = notesOf(draft), extraTiles = draft.extraTiles,
            )
        }

        if (sourcesProblem(draft) != null) return null
        val target = buildTarget(draft) ?: return null
        val moved = base != null && base.areaId != area
        val start = base ?: Goal(draft.id, area, "", draft.icon, draft.color, draft.kind, target, emptyList(), now, position, now)
        return start.copy(
            areaId = area,
            name = draft.name.trim(),
            icon = draft.icon,
            color = draft.color,
            kind = draft.kind,
            target = target,
            sources = buildSources(draft),
            position = if (base == null || moved) position else base.position,
            notes = notesOf(draft),
            extraTiles = draft.extraTiles,
        )
    }

    // ---------------------------------------------------------------- helpers

    /** A typed number ("1,5" and "1.5" both read), or null when blank or not a number. */
    fun number(text: String): Double? = text.trim().replace(',', '.').toDoubleOrNull()?.takeIf { it.isFinite() }

    private fun isBad(text: String, value: Double?): Boolean = text.isNotBlank() && value == null

    private fun maxDays(period: GoalPeriod): Int = when (period) {
        GoalPeriod.Month -> 31
        GoalPeriod.TwoWeeks -> 14
        else -> 7
    }

    // Whole numbers without a fraction, otherwise the shortest text ("1.5").
    private fun numberText(value: Double): String =
        if (value == Math.rint(value) && abs(value) < 1e9) value.toLong().toString() else value.toString().trimEnd('0').trimEnd('.')

    // A goal shell for the allowed-source check (what a source may be differs for Deep work).
    private fun shell(draft: GoalDraft): Goal {
        val epoch = OffsetDateTime.ofInstant(Instant.EPOCH, ZoneOffset.UTC)
        return Goal(
            draft.id, draft.areaId ?: draft.id, "", draft.icon, draft.color, draft.kind, GoalTarget(draft.period, null, GoalUnit.Count),
            emptyList(), epoch, 0.0, epoch, builtIn = draft.isDeepWork,
        )
    }
}
