package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.goals.PhoneGoalStatus
import com.accountabilibuddy.companion.stats.StatCard
import com.accountabilibuddy.companion.stats.StatsPeriod
import com.accountabilibuddy.companion.stats.StatsRange
import com.accountabilibuddy.companion.ui.goals.GoalIcons
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalFacts
import com.accountabilibuddy.shared.goals.GoalHeatmap
import com.accountabilibuddy.shared.goals.GoalHeatmapRange
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalStatsHistory
import com.accountabilibuddy.shared.goals.GoalStatsSeries
import com.accountabilibuddy.shared.goals.GoalTexts
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.util.UUID

/**
 * Stats for one goal (round 2 R6b4, owner Q554), the phone's version of the PC's Stats per goal: a chip per goal; the goal's icon,
 * name and "Learning · daily habit · 15 min a day · 6 months going", its age and streak and its paused stretches in words ("Paused
 * Sep 14 – Oct 30"); then the chosen range: a bar per day with the target line ([StatsGoalChart]), or for a year the goal's heatmap as
 * the PC draws it ([StatsGoalYear]), with the range's numbers under it. Paused days are hatched in both and left out of the numbers.
 * It only reads: nothing here ticks, logs, pauses or changes a goal.
 *
 * @param goals Null while the goals are being worked out.
 * @param period The dates the range covers, already settled by the pane.
 */
@Composable
fun StatsGoalContent(goals: StatsGoalsModel?, period: StatsPeriod, onPickGoal: (UUID) -> Unit, modifier: Modifier = Modifier) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(12.dp)) {
        val overview = goals?.overview
        val shown = goals?.shown
        val data = overview?.data
        when {
            goals?.status == PhoneGoalStatus.Unreadable -> StatsEmptyCard(
                "Goals can't be read",
                "Your goals on this phone can't be read (for example after the app's data was moved), so they are left as they are and nothing is changed.",
            )
            goals?.status == PhoneGoalStatus.NotSetUp -> StatsEmptyCard("No goals yet", "Your goals show up here once the Goals tab has set them up.")
            // The numbers are being worked out: the slot holds its place.
            goals == null || overview == null -> Box(Modifier.fillMaxWidth().height(LOADING_HEIGHT))
            shown == null || data == null -> StatsEmptyCard("No goals yet", "Add a goal on the Goals tab and its days show up here.")
            else -> {
                AbChipRow(contentPadding = PaddingValues(vertical = 2.dp)) {
                    overview.facts.forEach { facts ->
                        AbChip(facts.goal.name, selected = facts.goal.id == shown.goal.id, onClick = { onPickGoal(facts.goal.id) }, icon = GoalIcons.of(facts.goal.icon))
                    }
                }

                val tint = GoalIcons.color(shown.goal.color) ?: Ab.colors.accent
                val history = remember(shown, goals) { GoalStatsHistory.of(shown, goals.areaName(shown)) }
                GoalHistoryCard(shown, history, tint)
                val began = shown.goal.createdOn(goals.zone)
                if (period.to.isBefore(began)) {
                    StatsEmptyCard("This goal began ${GoalTexts.date(began, shown.today)}", "Nothing was asked of it before that.")
                } else {
                    GoalRangeCard(shown, data, goals, period, tint)
                }
            }
        }
    }
}

// The PC's header for a goal in Stats: icon chip, name, the line under it, its age and streak, then its paused stretches.
@Composable
private fun GoalHistoryCard(facts: GoalFacts, history: GoalStatsHistory, tint: Color) {
    val colors = Ab.colors
    AbCard(verticalSpacing = 12.dp) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(Modifier.size(40.dp).clip(RoundedCornerShape(12.dp)).background(tint.copy(alpha = 0.16f)), contentAlignment = Alignment.Center) {
                Icon(GoalIcons.of(facts.goal.icon), contentDescription = null, tint = tint, modifier = Modifier.size(22.dp))
            }
            Column(Modifier.weight(1f)) {
                Text(facts.goal.name, style = Ab.type.section, color = colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(history.subtitle, style = Ab.type.small, color = colors.muted, maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
        }

        Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(history.ageText, style = Ab.type.small, color = colors.muted)
            Text(history.streakText, style = Ab.type.small, color = colors.muted)
        }

        // Paused stretches: their own state, hatched, never a miss.
        if (history.pausedStretches.isNotEmpty()) {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                history.pausedStretches.forEach { text ->
                    Row(
                        Modifier.heightIn(min = 28.dp).background(colors.surface2, Ab.shapes.pill).border(1.dp, colors.line, Ab.shapes.pill).padding(horizontal = 10.dp, vertical = 4.dp),
                        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp),
                    ) {
                        HatchSwatch()
                        Text(text, style = Ab.type.small, color = colors.ink)
                    }
                }
            }
            Text(GoalStatsHistory.PAUSED_NOTE, style = Ab.type.micro, color = colors.muted)
        }
    }
}

// The range: the headline, the chart (bars, or the year's heatmap), the line that names a tapped day, and the numbers.
@Composable
private fun GoalRangeCard(facts: GoalFacts, data: GoalSourceData, goals: StatsGoalsModel, period: StatsPeriod, tint: Color) {
    val colors = Ab.colors
    val goal = facts.goal
    val today = facts.today
    val isYear = period.kind == StatsRange.Year
    // A year is drawn as the goal's heatmap (the PC's Stats per goal); its numbers are counted over the days it shows.
    val built by produceState<Pair<GoalStatsSeries, GoalHeatmap?>?>(null, goal, data, period, today, goals.weekStart) {
        value = withContext(Dispatchers.Default) {
            runCatching {
                if (isYear) {
                    val end = if (period.to.isAfter(today)) today else period.to
                    val map = GoalHeatmap.build(goal, data, today, goals.weekStart, GoalHeatmapRange.Year, end, YEAR_COLUMNS)
                    GoalStatsSeries.build(goal, data, map.grid.rangeStart, map.grid.end, today) to map
                } else {
                    GoalStatsSeries.build(goal, data, period.from, period.to, today) to null
                }
            }.getOrNull()
        }
    }
    val shown = built
    // The day last tapped, in words; a new goal or range clears it.
    var tapped by remember(goal.id, period) { mutableStateOf<String?>(null) }
    AbCard(verticalSpacing = 14.dp) {
        if (shown == null) {
            Box(Modifier.fillMaxWidth().height(LOADING_HEIGHT))
            return@AbCard
        }

        val (series, map) = shown
        Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Text(series.big, style = bigNumber(), color = colors.ink)
            Text(series.note, style = Ab.type.small, color = colors.muted, modifier = Modifier.weight(1f).padding(bottom = 4.dp))
        }

        if (map != null) {
            StatsGoalYear(map, goals.weekStart, goals.labels, tint, onCellClick = { tapped = it.tip })
        } else {
            StatsGoalChart(series, period, tint, today, onDayClick = { tapped = it.text })
        }

        // One line in a fixed slot, so nothing moves: the tapped day in words, else what the picture shows.
        Row(Modifier.heightIn(min = 20.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            val text = tapped
            if (text == null && series.pausedDays > 0) HatchSwatch()
            Text(
                text ?: listOfNotNull(if (series.pausedDays > 0) "Hatched = paused" else null, map?.legend, "Tap a day to read it").joinToString(" · "),
                style = Ab.type.micro, color = colors.muted, maxLines = 2, overflow = TextOverflow.Ellipsis,
            )
        }

        StatTiles(series.tiles.map { StatCard(it.title, it.value, it.detail) })
    }
}

// The key for a paused day: a small hatched square, as the PC's pill draws it.
@Composable
private fun HatchSwatch() {
    val line = Ab.colors.muted
    val edge = Ab.colors.faint
    Canvas(Modifier.size(12.dp).clip(RoundedCornerShape(3.dp)).border(1.dp, edge, RoundedCornerShape(3.dp)).clearAndSetSemantics { }) {
        val step = 4.dp.toPx()
        var x = -size.height
        while (x < size.width) {
            drawLine(line, Offset(x, size.height), Offset(x + size.height, 0f), 1.dp.toPx())
            x += step
        }
    }
}

// Wide enough for a whole year of weeks (53, and one more when the range starts mid-week), so nothing is cropped: the grid scrolls.
private const val YEAR_COLUMNS = 60

private val LOADING_HEIGHT = 260.dp
