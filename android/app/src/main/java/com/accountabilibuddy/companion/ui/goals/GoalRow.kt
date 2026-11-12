package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.MoreVert
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalFacts
import com.accountabilibuddy.shared.goals.GoalNumbers
import com.accountabilibuddy.shared.goals.GoalRowVisual
import com.accountabilibuddy.shared.goals.GoalStrip14
import com.accountabilibuddy.shared.goals.GoalTexts

/**
 * One goal on the phone's Goals list (brief §4.3, change note §4a): the icon on a chip tinted with the goal's colour, the name, the
 * target under it ("↳ 15 min a day"; "Built in" as a chip on Deep work), and at the right the one number that matters with the kind's
 * visual under it. Status pills share the number's slot: an amber Behind takes its place, a muted "Due Fri" or Paused sits beside it.
 * A paused goal (All only) is faded with "since Mon 10/12". A tap opens the goal; a hold or ⋮ opens the goal menu (R6b).
 *
 * @param noPcGoal Deep work has no daily target yet because no PC file has carried one: its line says where it comes from.
 */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun GoalRow(facts: GoalFacts, noPcGoal: Boolean, onOpen: () -> Unit, onMenu: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val goal = facts.goal
    val tint = GoalIcons.color(goal.color) ?: colors.accent
    val pill = GoalNumbers.pill(facts)
    val amber = pill?.second == true
    val number = GoalNumbers.number(facts)
    val subLine = if (goal.isDeepWork && noPcGoal) "daily target from your PC" else GoalTexts.targetLine(goal, facts.today)
    Row(
        modifier
            .fillMaxWidth()
            .heightIn(min = 64.dp)
            .combinedClickable(role = Role.Button, onClickLabel = "Open", onLongClickLabel = "Goal menu", onLongClick = onMenu, onClick = onOpen)
            .alpha(if (facts.isPaused) 0.55f else 1f)
            .padding(start = 16.dp, top = 8.dp, bottom = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(Modifier.size(40.dp).clip(RoundedCornerShape(12.dp)).background(tint.copy(alpha = 0.16f)), contentAlignment = Alignment.Center) {
            Icon(GoalIcons.of(goal.icon), contentDescription = null, tint = tint, modifier = Modifier.size(22.dp))
        }
        Column(Modifier.weight(1f)) {
            Text(goal.name, style = Ab.type.bodyMedium.copy(fontSize = 15.5.sp), fontWeight = FontWeight.Medium, color = colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("↳ $subLine", style = Ab.type.small, color = colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
                if (goal.isDeepWork) MutedPill("Built in")
            }
            val since = facts.pausedSince
            if (facts.isPaused && since != null) {
                Text("since ${GoalTexts.date(since, facts.today)}", style = Ab.type.micro, color = colors.faint)
            }
        }
        Column(horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                if (pill != null && !amber) MutedPill(pill.first)
                if (amber) AmberPill(pill.first) else Text(number, style = Ab.type.small.copy(fontFeatureSettings = "tnum"), fontWeight = FontWeight.SemiBold, color = colors.ink, maxLines = 1)
            }
            Visual(facts, tint)
        }
        IconButton(onClick = onMenu) {
            Icon(Icons.Outlined.MoreVert, contentDescription = "Goal menu: ${goal.name}", tint = colors.muted)
        }
    }
}

@Composable
private fun Visual(facts: GoalFacts, tint: Color) {
    when (GoalNumbers.visual(facts)) {
        GoalRowVisual.Bar -> GoalProgressBar(GoalNumbers.fraction(facts).takeUnless { it.isNaN() } ?: 0.0, tint)
        GoalRowVisual.Strip -> GoalStripVisual(facts.strip, tint)
        GoalRowVisual.WeekDots -> GoalWeekDots(facts.periodCells, tint)
        GoalRowVisual.DayBars -> {
            val week = facts.recent.takeLast(7)
            val amount = facts.goal.target.amount ?: 1.0
            GoalDayBars(week.map { GoalStrip14.cellOf(facts.goal, it, facts.today) }, week.map { if (amount > 0) it.value / amount else 0.0 }, tint)
        }
        GoalRowVisual.Sparkline -> GoalSparkline(facts.recent.map { if (it.hasValue) it.value else null })
        GoalRowVisual.None -> Unit
    }
}

/** "Built in", "Due Fri", "Paused": a small muted pill. */
@Composable
fun MutedPill(text: String) {
    val colors = Ab.colors
    Text(
        text, style = Ab.type.micro, color = colors.muted, maxLines = 1,
        modifier = Modifier.clip(Ab.shapes.pill).background(colors.surface2).padding(horizontal = 7.dp, vertical = 2.dp),
    )
}

/** "Behind": amber means late or behind (brief §3.8). */
@Composable
fun AmberPill(text: String) {
    val colors = Ab.colors
    Text(
        text, style = Ab.type.micro, fontWeight = FontWeight.SemiBold, color = colors.amberInk, maxLines = 1,
        modifier = Modifier.clip(Ab.shapes.pill).background(colors.amberFill).border(1.dp, colors.amberLine, Ab.shapes.pill)
            .padding(horizontal = 8.dp, vertical = 2.dp),
    )
}
