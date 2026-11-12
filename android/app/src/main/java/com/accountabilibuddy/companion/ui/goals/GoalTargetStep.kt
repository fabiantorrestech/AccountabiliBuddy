package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material.icons.outlined.NotificationsNone
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.goals.GoalDisplayPrefs
import com.accountabilibuddy.companion.goals.PhoneDeepWork
import com.accountabilibuddy.companion.goals.PhoneGoalData
import com.accountabilibuddy.companion.guidance.isCompanionMode
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.sheets.AbSheetValue
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeekly
import com.accountabilibuddy.shared.goals.GoalDirection
import com.accountabilibuddy.shared.goals.GoalDraft
import com.accountabilibuddy.shared.goals.GoalDrafts
import com.accountabilibuddy.shared.goals.GoalFactsBuilder
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalLabelId
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalTakesText
import com.accountabilibuddy.shared.goals.GoalTexts
import com.accountabilibuddy.shared.goals.GoalUnit
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * Step 2 of New goal / Edit goal ("2 of 2 · how it's measured", board Goals-Phone-New-2; also "Change the target" and the weight page's
 * Target): TARGET (Daily / Weekly / Monthly and what each kind asks for), the optional long-term target with its date, the live
 * **What it takes** box (green when covered, amber when not), WHAT COUNTS (the sources as chips, "+ Source") and a Reminders row that says
 * it comes in the next update. Create (or Save) stays off until [GoalDrafts.canCreate].
 *
 * Deep work shows its daily target read only ("set on your PC", ADR 29) and the weekly target as the one thing to change; a waiting
 * lowering shows with Cancel. Every other goal's changes apply at once.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun GoalTargetStep(
    title: String,
    draft: GoalDraft,
    onDraft: (GoalDraft) -> Unit,
    stored: Goal?,
    weekly: GoalDeepWorkWeekly,
    labels: List<String>,
    allReceived: List<ReceivedDevice>,
    received: List<ReceivedDevice>,
    areaName: String?,
    onSources: () -> Unit,
    onCancelWeekly: () -> Unit,
    onBack: () -> Unit,
    onDismiss: () -> Unit,
    onSave: () -> Unit,
) {
    val colors = Ab.colors
    val companion = isCompanionMode()
    val problem = GoalDrafts.basicsProblem(draft) ?: GoalDrafts.targetProblem(draft) ?: GoalDrafts.sourcesProblem(draft)
    AbSheet(
        title = title,
        subtitle = "2 of 2 · ${draft.name.trim().ifEmpty { "your goal" }} · how it's measured",
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        onBack = onBack,
        footer = {
            problem?.let { Text(it, style = Ab.type.small, color = colors.muted) }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                AbTonalButton("Back", onClick = onBack, modifier = Modifier.weight(1f))
                AbPrimaryButton(if (stored == null) "Create goal" else "Save", onClick = onSave, enabled = problem == null, modifier = Modifier.weight(1f))
            }
        },
    ) {
        if (draft.isDeepWork) {
            DeepWorkTarget(draft, onDraft, weekly, received, companion, onCancelWeekly)
            return@AbSheet
        }

        GoalFieldLabel("TARGET")
        val periods = GoalDrafts.PERIODS
        AbSegmented(
            listOf("Daily", "Weekly", "Monthly"), periods.indexOf(draft.period).takeIf { it >= 0 },
            { onDraft(draft.copy(period = periods[it])) }, Modifier.padding(horizontal = 24.dp),
        )
        if (draft.period == GoalPeriod.TwoWeeks) {
            Text("Every 2 weeks (kept from before; pick another to change it)", style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 4.dp))
        }

        Fields(draft, onDraft)

        GoalFieldLabel("LONG-TERM TARGET (OPTIONAL)")
        LongTermFields(draft, onDraft)

        TakesBox(draft, stored, allReceived)

        GoalFieldLabel("WHAT COUNTS")
        val byId = remember(labels) { labels.associateBy { GoalLabelId.idFor(it) } }
        FlowRow(Modifier.padding(horizontal = 24.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            draft.sources.forEach { rule ->
                AbChip(
                    sourceName(rule.type, rule.labelId?.let(byId::get)), selected = true,
                    onClick = { onDraft(draft.copy(sources = draft.sources.filter { it.id != rule.id })) }, icon = Icons.Outlined.Close,
                )
            }
            AbChip("Source", selected = false, onClick = onSources, icon = Icons.Outlined.Add)
        }
        Text(
            "Tap a source to take it away. Sources add up · overlapping time counts once · your edits win.",
            style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(start = 24.dp, end = 24.dp, top = 6.dp),
        )

        GoalFieldLabel("REMINDERS")
        AbSheetRow("Reminders", onClick = null, icon = Icons.Outlined.NotificationsNone, enabled = false, subtitle = "Coming in the next update")

        Text(
            "Raising or lowering this goal's target applies at once: it feeds no guard.",
            style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 12.dp),
        )
    }
}

// The target's own fields for the goal's kind and period.
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun Fields(draft: GoalDraft, onDraft: (GoalDraft) -> Unit) {
    val colors = Ab.colors
    val each = when (draft.period) {
        GoalPeriod.Day -> "Each day"
        GoalPeriod.Week -> "Each week"
        GoalPeriod.TwoWeeks -> "Every 2 weeks"
        GoalPeriod.Month -> "Each month"
    }
    val field = Modifier.padding(horizontal = 24.dp, vertical = 6.dp)
    when {
        GoalDrafts.isHabit(draft) && draft.period == GoalPeriod.Day ->
            AbTextField(draft.threshold, { onDraft(draft.copy(threshold = it)) }, "Ticks itself at (minutes, optional)", field, keyboardType = KeyboardType.Number)
        GoalDrafts.showsTimesPerPeriod(draft) ->
            AbTextField(draft.amount, { onDraft(draft.copy(amount = it)) }, "Times ${each.lowercase()}", field, keyboardType = KeyboardType.Number)
        GoalDrafts.showsAmount(draft) -> {
            if (draft.kind == GoalKind.Amount) {
                val units = GoalDrafts.AMOUNT_UNITS
                AbSegmented(listOf("Count", "Steps", "lb", "kg"), units.indexOf(draft.unit).takeIf { it >= 0 }, { onDraft(draft.copy(unit = units[it])) }, Modifier.padding(horizontal = 24.dp, vertical = 6.dp))
            }
            val unitText = if (draft.kind == GoalKind.Time) "minutes" else unitText(draft.unit)
            AbTextField(draft.amount, { onDraft(draft.copy(amount = it)) }, "$each ($unitText)", field, keyboardType = KeyboardType.Decimal)
            val directions = if (draft.kind == GoalKind.Amount) listOf(GoalDirection.AtLeast, GoalDirection.AtMost, GoalDirection.Reach) else listOf(GoalDirection.AtLeast, GoalDirection.AtMost)
            AbSegmented(
                directions.map { when (it) { GoalDirection.AtLeast -> "At least"; GoalDirection.AtMost -> "At most"; GoalDirection.Reach -> "Reach" } },
                directions.indexOf(draft.direction).takeIf { it >= 0 }, { onDraft(draft.copy(direction = directions[it])) }, Modifier.padding(horizontal = 24.dp, vertical = 6.dp),
            )
            if (GoalDrafts.showsWeeklyDays(draft)) {
                AbTextField(draft.weeklyDays, { onDraft(draft.copy(weeklyDays = it)) }, "On how many days (optional)", field, keyboardType = KeyboardType.Number)
            }
        }
        else -> Text(
            if (draft.kind == GoalKind.Milestone) "A milestone is done by hand; add a long-term target below if it counts something." else "Ticked by hand.",
            style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 8.dp),
        )
    }
}

@Composable
private fun LongTermFields(draft: GoalDraft, onDraft: (GoalDraft) -> Unit) {
    val unit = when (draft.kind) {
        GoalKind.Time -> "hours"
        GoalKind.Habit -> "days"
        GoalKind.Milestone -> "count"
        GoalKind.Amount -> unitText(draft.unit)
    }
    val field = Modifier.padding(horizontal = 24.dp, vertical = 6.dp)
    AbTextField(draft.longTerm, { onDraft(draft.copy(longTerm = it)) }, "Reach ($unit)", field, keyboardType = KeyboardType.Decimal)
    GoalDateRow(draft.targetDate, onPick = { onDraft(draft.copy(targetDate = it)) }, enabled = draft.longTerm.isNotBlank())
}

// What it takes: worked out off the main thread for the goal as the sheet has it now (the shared maths, as the goal page's tile).
@Composable
private fun TakesBox(draft: GoalDraft, stored: Goal?, allReceived: List<ReceivedDevice>) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val display by GoalDisplayPrefs.display.collectAsStateWithLifecycle()
    val steps by app.steps.state.collectAsStateWithLifecycle()
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    val goal = remember(draft, stored) {
        GoalDrafts.toGoal(draft.copy(name = draft.name.ifBlank { "Goal" }, areaId = draft.areaId ?: draft.id), stored, OffsetDateTime.now(), 1.0)
    }
    val takes by produceState<GoalTakesText?>(null, goal, display.weekStart, allReceived, steps) {
        value = if (goal == null) null else withContext(Dispatchers.Default) {
            runCatching {
                val zone = ZoneId.systemDefault()
                val now = Instant.now()
                val data = PhoneGoalData.build(allReceived.map { it.file }, listOf(goal), lists.liveEntries.filter { it.goalId == goal.id }, steps, now, zone)
                val today = now.atZone(zone).toLocalDate()
                GoalFactsBuilder.build(goal, data, today, display.weekStart).whatItTakes?.let { GoalTakesText.of(it, goal, today) }
            }.getOrNull()
        }
    }
    val shown = takes ?: return
    val colors = Ab.colors
    Text("WHAT IT TAKES", style = Ab.type.micro, color = colors.muted, modifier = Modifier.padding(start = 24.dp, top = 14.dp, bottom = 6.dp))
    AbCard(Modifier.padding(horizontal = 24.dp), verticalSpacing = 4.dp) {
        Text(shown.remaining, style = Ab.type.small, color = colors.ink)
        shown.each?.let { Text(it, style = Ab.type.small, color = colors.muted) }
        shown.pace?.let { Text(it, style = Ab.type.small, color = colors.muted) }
        Text(shown.verdict, style = Ab.type.bodyMedium, color = if (shown.green) colors.met else if (shown.amber) colors.amberInk else colors.muted)
    }
}

// Deep work: the daily target is the PC's and read only; the weekly target is this phone's, and lowering it waits.
@Composable
private fun DeepWorkTarget(draft: GoalDraft, onDraft: (GoalDraft) -> Unit, weekly: GoalDeepWorkWeekly, received: List<ReceivedDevice>, companion: Boolean, onCancelWeekly: () -> Unit) {
    val colors = Ab.colors
    val pcDaily = PhoneDeepWork.pcDailyMinutes(received.map { it.file })
    GoalFieldLabel("TARGET")
    AbSheetRow(
        "Daily target", onClick = null, icon = Icons.Outlined.Timer, enabled = false,
        subtitle = "Set on your PC (it can't be changed here)",
        trailing = { AbSheetValue(pcDaily?.let { GoalTexts.minutes(it.toDouble()) } ?: "Not set yet", opens = false) },
    )
    AbTextField(
        draft.weeklyHours, { onDraft(draft.copy(weeklyHours = it)) }, "Weekly target (hours, optional)",
        Modifier.padding(horizontal = 24.dp, vertical = 6.dp), keyboardType = KeyboardType.Decimal,
    )
    weekly.pending?.let { pending ->
        val at = pending.appliesAt.atZoneSameInstant(ZoneId.systemDefault()).format(CLOCK)
        val to = pending.amount?.let { GoalTexts.minutes(it) } ?: "no target"
        AbSheetRow(
            "Lowers to $to at $at", onClick = null, icon = Icons.Outlined.Schedule,
            subtitle = "Never during a session · no apply now",
            trailing = { AbTextButton("Cancel", onClick = onCancelWeekly) },
        )
    }
    Text(
        if (companion) {
            "Raising a target applies at once. In ProductaBuddy mode lowering one applies at once too."
        } else {
            "Raising a target applies at once. Lowering one waits (10 min) and never happens during a session; you can't ask for it during one."
        },
        style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 12.dp),
    )
    AbSheetRow("Counts all focus", onClick = null, icon = Icons.Outlined.Lock, enabled = false, subtitle = "Deep work's source is locked to focus")
}

/** A source's name on a chip: "Spanish" for a label, "Tick by hand", "Daily steps", "Sleep". */
internal fun sourceName(type: GoalSourceType, labelName: String?): String = when (type) {
    GoalSourceType.FocusAll -> "All focus"
    GoalSourceType.FocusLabel -> labelName ?: "A label"
    GoalSourceType.FocusTag -> "#tag"
    GoalSourceType.Hand -> "Tick by hand"
    GoalSourceType.HealthSteps -> "Daily steps"
    GoalSourceType.HealthSleep -> "Sleep"
    GoalSourceType.HealthWeight -> "Weight"
    GoalSourceType.ScreenTime -> "Screen time"
    GoalSourceType.CalendarCategory -> "Calendar"
    GoalSourceType.Beeminder -> "Beeminder"
    GoalSourceType.GoalField -> "Task goal"
    GoalSourceType.MilestoneTasks -> "Milestone tasks"
}

private fun unitText(unit: GoalUnit): String = when (unit) {
    GoalUnit.Steps -> "steps"
    GoalUnit.Lb -> "lb"
    GoalUnit.Kg -> "kg"
    GoalUnit.Minutes -> "minutes"
    else -> "times"
}

private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)
