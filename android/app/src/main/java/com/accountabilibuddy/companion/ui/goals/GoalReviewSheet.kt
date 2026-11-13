package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.automirrored.outlined.ArrowForward
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.CleaningServices
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material.icons.outlined.PauseCircle
import androidx.compose.material.icons.outlined.SkipNext
import androidx.compose.material.icons.outlined.Snooze
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.goals.GoalDisplayPrefs
import com.accountabilibuddy.companion.goals.GoalSettingsPrefs
import com.accountabilibuddy.companion.goals.PhoneGoalReview
import com.accountabilibuddy.companion.goals.PhoneGoalsOverview
import com.accountabilibuddy.companion.guidance.GuidanceStore
import com.accountabilibuddy.companion.ui.LocalGuardsLock
import com.accountabilibuddy.companion.ui.kit.AbOutlinedButton
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalReviewAdjustment
import com.accountabilibuddy.shared.goals.GoalReviewChoice
import com.accountabilibuddy.shared.goals.GoalReviewFacts
import com.accountabilibuddy.shared.goals.GoalReviewRules
import com.accountabilibuddy.shared.goals.GoalReviewSuggestion
import com.accountabilibuddy.shared.goals.GoalReviewWeek
import java.util.UUID

/**
 * The weekly goals review on the phone (round 2 R6b3; brief §4.3, §5.8, §5.10; board Goals-Phone-Review): an 80% sheet, "Weekly goals review"
 * over "2 of 8 · week of Nov 9–15"; one goal at a time on a card (name, ← 2 / 8 →, its area and kind, the facts the suggestion used as
 * pills, amber when behind; a habit's week as seven boxes with "**2 of 3** · one more would have made it"); the brief's four numbered
 * decisions (1 Keep going, 2 Adjust the target, 3 Pause this goal, 4 Decide next week; "Suggested" on the rule-based suggestion, the
 * highlighted row with ❯ and an accent border); "Then the tidy-up" greyed when Settings › Goals runs it straight after (the phone's tidy-up
 * comes with Tasks); and Snooze ⌄ / Stop for now.
 *
 * Each answer is kept at once in the week's record ([PhoneGoalReview.decide]), so Stop for now keeps them and the review opens again at
 * the next unanswered goal. Pause pauses at once and is refused for Deep work; Adjust records the answer, then opens Edit goal › Target
 * with the suggested numbers ([onAdjust]; a lower Deep work weekly target still waits). A focus session closes it (answers kept).
 * Goal names are the user's text: shown here only.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun GoalReviewSheet(overview: PhoneGoalsOverview, onDismiss: () -> Unit, onAdjust: (UUID, GoalReviewAdjustment?) -> Unit, onMessage: (String) -> Unit) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val colors = Ab.colors
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    val settings by GoalSettingsPrefs.review.collectAsStateWithLifecycle()
    val display by GoalDisplayPrefs.display.collectAsStateWithLifecycle()
    val lock = LocalGuardsLock.current
    // Never during a session: one that starts closes the review; the answers given stay.
    LaunchedEffect(lock) { if (lock != null) onDismiss() }

    // The goals asked about are fixed when it opens (a goal paused here stays in the count, as on the PC).
    val opened = remember { PhoneGoalReview.moment(app, overview.facts.map { it.goal }) }
    val asked = opened.goals
    val week = opened.week
    val record = GoalReviewRules.recordFor(lists.reviews, week)
    var index by rememberSaveable { mutableStateOf(GoalReviewRules.firstUnanswered(record, asked, 0) ?: 0) }
    var finished by rememberSaveable { mutableStateOf(asked.isEmpty() || GoalReviewRules.isComplete(record, asked)) }
    var message by remember { mutableStateOf<String?>(null) }
    var snoozeMenu by remember { mutableStateOf(false) }
    val weekText = GoalReviewRules.weekText(week, compact = true)
    val data = overview.data

    AbSheet(
        title = "Weekly goals review",
        subtitle = if (finished || asked.isEmpty()) "week of $weekText" else "${index + 1} of ${asked.size} · week of $weekText",
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        footer = {
            if (finished) {
                AbPrimaryButton("Done", onClick = onDismiss, modifier = Modifier.fillMaxWidth())
            } else {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Box(Modifier.weight(1f)) {
                        AbOutlinedButton("Snooze", onClick = { snoozeMenu = true }, icon = Icons.Outlined.Snooze, modifier = Modifier.fillMaxWidth())
                        Icon(Icons.Outlined.ExpandMore, contentDescription = null, tint = colors.muted, modifier = Modifier.align(Alignment.CenterEnd).padding(end = 14.dp).size(18.dp))
                        GoalReviewSnoozeMenu(snoozeMenu, opened, settings.snoozeDays, onDismiss = { snoozeMenu = false }, onPick = { choice ->
                            snoozeMenu = false
                            val error = PhoneGoalReview.snooze(app, choice)
                            if (error != null) onMessage(error) else onDismiss()
                        })
                    }
                    AbOutlinedButton("Stop for now", onClick = onDismiss, icon = Icons.Outlined.PauseCircle, modifier = Modifier.weight(1f))
                }
            }
        },
    ) {
        if (finished || data == null) {
            Text(
                when {
                    asked.isEmpty() -> "No goals to review this week."
                    data == null -> "The numbers are still being worked out. Try again in a moment."
                    asked.size == 1 -> "Reviewed 1 goal. See you next week."
                    else -> "Reviewed ${asked.size} goals. See you next week."
                },
                style = Ab.type.body, color = colors.ink, modifier = Modifier.padding(horizontal = 24.dp, vertical = 16.dp),
            )
            return@AbSheet
        }

        val goal = asked[index.coerceIn(0, asked.lastIndex)]
        val today = overview.today
        val suggestion = remember(goal.id, data) { GoalReviewSuggestion.forGoal(goal, data, today, display.weekStart) }
        val facts = remember(goal.id, data) { GoalReviewFacts.of(goal, suggestion, data, today) }
        val boxes = remember(goal.id, data, week) { GoalReviewWeek.of(goal, data, week, today) }
        val answered = GoalReviewRules.answerOf(record, goal.id)
        val areaName = lists.liveAreas.firstOrNull { it.id == goal.areaId }?.name

        GoalCardBlock(goal, areaName, facts.map { it.text to it.amber }, boxes, index, asked.size, onBack = { index--; message = null }, onForward = { index++; message = null })

        Column(Modifier.padding(start = 16.dp, end = 16.dp, top = 14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            val suggestedChoice = suggestion.choice.takeIf { GoalReviewRules.refusal(goal, it) == null } ?: GoalReviewChoice.KeepGoing
            val lit = answered ?: suggestedChoice
            GoalReviewChoice.entries.forEachIndexed { at, choice ->
                val refused = GoalReviewRules.refusal(goal, choice)
                DecisionRow(
                    number = at + 1,
                    choice = choice,
                    sub = GoalReviewRules.choiceSub(choice, goal, opened.productaBuddy, GuidanceStore.DELAY.toMinutes()),
                    lit = choice == lit,
                    suggested = answered == null && choice == suggestedChoice && suggestion.choice == choice,
                    chosen = answered == choice,
                    enabled = refused == null,
                    onClick = {
                        val error = PhoneGoalReview.decide(app, week, goal, choice)
                        if (error != null) {
                            message = error
                        } else {
                            message = null
                            if (choice == GoalReviewChoice.Adjust) onAdjust(goal.id, GoalReviewAdjustment.forGoal(goal, suggestion, today))
                            val saved = GoalReviewRules.recordFor(app.goals.lists.value.reviews, week)
                            val next = GoalReviewRules.firstUnanswered(saved, asked, index + 1)
                            if (next == null) finished = true else index = next
                        }
                    },
                )
            }
            message?.let { Text(it, style = Ab.type.small, color = colors.amberInk) }
        }

        if (settings.tidyUpAfter) {
            // The tidy-up flow arrives on the phone with Tasks (R7): the line is shown greyed until then.
            Row(
                Modifier.padding(horizontal = 20.dp, vertical = 14.dp).alpha(0.6f),
                verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Icon(Icons.Outlined.CleaningServices, contentDescription = null, tint = colors.muted, modifier = Modifier.size(18.dp))
                Text(if (overview.hasPc) "Then the tidy-up · about 2 minutes · on your PC for now" else "Then the tidy-up · about 2 minutes · not on the phone yet", style = Ab.type.small, color = colors.muted)
            }
        }
    }
}

// The goal card (board: name 21 sp / 600, ← 2 / 8 → at 34 dp, tags, then a habit's week boxes and its line).
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun GoalCardBlock(
    goal: Goal,
    areaName: String?,
    facts: List<Pair<String, Boolean>>,
    boxes: GoalReviewWeek?,
    index: Int,
    total: Int,
    onBack: () -> Unit,
    onForward: () -> Unit,
) {
    val colors = Ab.colors
    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp).clip(RoundedCornerShape(20.dp)).background(colors.surface2).padding(start = 16.dp, end = 8.dp, top = 14.dp, bottom = 14.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(goal.name, fontSize = 21.sp, fontWeight = FontWeight.SemiBold, color = colors.ink, modifier = Modifier.weight(1f))
            IconButton(onClick = onBack, enabled = index > 0, modifier = Modifier.size(34.dp)) {
                Icon(Icons.AutoMirrored.Outlined.ArrowBack, contentDescription = "Previous goal", tint = if (index > 0) colors.ink else colors.faint, modifier = Modifier.size(18.dp))
            }
            Text("${index + 1} / $total", style = Ab.type.small.copy(fontFeatureSettings = "tnum"), color = colors.muted)
            IconButton(onClick = onForward, enabled = index < total - 1, modifier = Modifier.size(34.dp)) {
                Icon(Icons.AutoMirrored.Outlined.ArrowForward, contentDescription = "Next goal", tint = if (index < total - 1) colors.ink else colors.faint, modifier = Modifier.size(18.dp))
            }
        }
        FlowRow(Modifier.padding(top = 6.dp, bottom = 12.dp, end = 8.dp), horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            areaName?.let { Tag(it) }
            Tag(kindChip(goal))
            facts.forEach { (text, amber) -> Tag(text, amber) }
        }
        if (boxes != null) {
            Row(Modifier.fillMaxWidth().padding(end = 8.dp), horizontalArrangement = Arrangement.SpaceBetween) {
                boxes.cells.forEach { cell ->
                    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        val shape = RoundedCornerShape(10.dp)
                        Box(
                            Modifier.size(30.dp).clip(shape).background(if (cell.hit) colors.met else colors.bg).border(1.dp, if (cell.hit) colors.met else colors.line, shape).alpha(if (cell.counted) 1f else 0.4f),
                            contentAlignment = Alignment.Center,
                        ) {
                            if (cell.hit) Icon(Icons.Outlined.Check, contentDescription = "Hit", tint = colors.bg, modifier = Modifier.size(16.dp))
                        }
                        Text(cell.letter, fontSize = 12.sp, color = colors.muted)
                    }
                }
            }
            Text(
                buildAnnotatedString {
                    withStyle(SpanStyle(fontWeight = FontWeight.Bold, color = colors.ink)) { append(boxes.bold) }
                    withStyle(SpanStyle(color = colors.muted)) { append(" · ${boxes.rest}") }
                },
                fontSize = 14.sp, modifier = Modifier.padding(top = 12.dp),
            )
        }
    }
}

@Composable
private fun Tag(text: String, amber: Boolean = false) {
    val colors = Ab.colors
    Text(
        text,
        fontSize = 11.sp, fontWeight = FontWeight.SemiBold,
        color = if (amber) colors.amberInk else colors.muted,
        modifier = Modifier.clip(Ab.shapes.pill).background(if (amber) colors.amberFill else colors.surface)
            .let { if (amber) it.border(1.dp, colors.amberLine, Ab.shapes.pill) else it }.padding(horizontal = 8.dp, vertical = 2.dp),
    )
}

// One numbered decision: 58 dp, a 1.5 dp border; the highlighted row has ❯ and the accent border; "Suggested" on the suggestion.
@Composable
private fun DecisionRow(number: Int, choice: GoalReviewChoice, sub: String, lit: Boolean, suggested: Boolean, chosen: Boolean, enabled: Boolean, onClick: () -> Unit) {
    val colors = Ab.colors
    val shape = RoundedCornerShape(14.dp)
    Row(
        Modifier.fillMaxWidth().heightIn(min = 58.dp).clip(shape)
            .background(if (lit && enabled) colors.accent.copy(alpha = 0.14f) else colors.bg.copy(alpha = 0f))
            .border(1.5.dp, if (lit && enabled) colors.accent else colors.line, shape)
            .clickable(enabled = enabled, role = Role.Button, onClick = onClick)
            .alpha(if (enabled) 1f else 0.5f)
            .padding(horizontal = 14.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Text(if (lit && enabled) "❯" else "", color = colors.accent, modifier = Modifier.width(12.dp))
        Text("$number.", fontSize = 14.sp, color = colors.muted, modifier = Modifier.width(18.dp))
        Icon(
            when (choice) {
                GoalReviewChoice.KeepGoing -> Icons.Outlined.Check
                GoalReviewChoice.Adjust -> Icons.Outlined.Edit
                GoalReviewChoice.Pause -> Icons.Outlined.PauseCircle
                GoalReviewChoice.DecideNextWeek -> Icons.Outlined.SkipNext
            },
            contentDescription = null, tint = colors.muted, modifier = Modifier.size(20.dp),
        )
        Column(Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(GoalReviewRules.choiceTitle(choice), fontSize = 15.5.sp, fontWeight = FontWeight.SemiBold, color = colors.ink)
                if (suggested) {
                    Text("Suggested", fontSize = 11.sp, fontWeight = FontWeight.SemiBold, color = colors.onAccent, modifier = Modifier.clip(Ab.shapes.pill).background(colors.accent).padding(horizontal = 8.dp, vertical = 2.dp))
                }
                if (chosen) {
                    Text("Your choice", fontSize = 11.sp, fontWeight = FontWeight.SemiBold, color = colors.muted, modifier = Modifier.clip(Ab.shapes.pill).background(colors.surface2).padding(horizontal = 8.dp, vertical = 2.dp))
                }
            }
            Text(sub, fontSize = 13.sp, color = colors.muted)
        }
    }
}
