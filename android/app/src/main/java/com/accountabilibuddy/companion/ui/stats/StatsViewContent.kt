package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.stats.ConsistencyPanels
import com.accountabilibuddy.companion.stats.DayPanels
import com.accountabilibuddy.companion.stats.DriftPanels
import com.accountabilibuddy.companion.stats.IntensityPanels
import com.accountabilibuddy.companion.stats.OverviewPanels
import com.accountabilibuddy.companion.stats.PhonePanels
import com.accountabilibuddy.companion.stats.SleepPanels
import com.accountabilibuddy.companion.stats.StatsRange
import com.accountabilibuddy.companion.stats.StatsView
import com.accountabilibuddy.companion.stats.TrendPanels
import java.time.LocalDate

/**
 * The body of one Stats view (list and detail are separate composables, so the tablet can put the views in a left column and this
 * beside it). [range] is the one this view shows, already settled by the pane. [day] is the day the Overview's Day range shows.
 * Every view says plainly what is missing instead of drawing zeros that were never measured.
 */
@Composable
fun StatsViewContent(
    model: StatsPaneModel,
    view: StatsView,
    range: StatsRange?,
    day: LocalDate,
    actions: StatsPaneActions,
    onPickDay: (LocalDate) -> Unit,
    modifier: Modifier = Modifier,
) {
    val data = model.data
    Column(modifier, verticalArrangement = Arrangement.spacedBy(12.dp)) {
        when (view) {
            StatsView.Overview -> {
                val extra = remember(model) { listOfNotNull(PhonePanels.card(model.phoneDays, data.today), SleepPanels.card(model.nights, data.today)) }
                val panel = remember(model, range, day) { OverviewPanels.build(data, range ?: StatsRange.Week, day, extra) }
                if (panel == null) {
                    StatsEmptyCard("Nothing to show yet", emptyBody(model, needsGoal = false))
                } else {
                    BarPanelCard(panel, onBarClick = onPickDay, onOpenView = actions.onSelectView)
                    if (range == StatsRange.Day) {
                        val detail = remember(model, day) { DayPanels.build(data, day, model.sleepOn) }
                        StatsDayDetail(
                            detail,
                            onPrevious = if (day.isAfter(data.firstData)) ({ onPickDay(day.minusDays(1)) }) else null,
                            onNext = if (day.isBefore(data.today)) ({ onPickDay(day.plusDays(1)) }) else null,
                        )
                    } else {
                        StatsDaysCard(panel.days, onDayClick = onPickDay)
                    }
                }
            }

            StatsView.Consistency -> {
                val panel = remember(model, range) { ConsistencyPanels.build(data, range ?: StatsRange.Year) }
                if (panel == null) StatsEmptyCard("Nothing to show yet", emptyBody(model, needsGoal = true)) else HeatPanelCard(panel, onDayClick = onPickDay)
            }

            StatsView.Intensity -> {
                val panel = remember(model, range) { IntensityPanels.build(data, range ?: StatsRange.Month) }
                if (panel == null) StatsEmptyCard("Nothing to show yet", emptyBody(model, needsGoal = false)) else HeatPanelCard(panel)
            }

            StatsView.FocusGraph -> {
                val panel = remember(model, range) { TrendPanels.build(data, range ?: StatsRange.Month) }
                if (panel == null) StatsEmptyCard("Nothing to show yet", emptyBody(model, needsGoal = true)) else TrendPanelCard(panel)
            }

            StatsView.Drift -> {
                val panel = remember(model, range) { DriftPanels.build(data, range ?: StatsRange.Month) }
                if (panel == null) StatsEmptyCard("Nothing to show yet", emptyBody(model, needsGoal = false)) else BarPanelCard(panel, onBarClick = onPickDay)
            }

            StatsView.Sleep -> {
                val panel = remember(model) { SleepPanels.build(model.nights, model.sleepGoal, data.today) }
                if (panel == null) {
                    StatsEmptyCard("No measured sleep yet", "Nights show up here once your watch or phone has recorded some and Health Connect is connected.")
                } else {
                    BarPanelCard(panel)
                }

                StatsSleepCard(model.sleepNight, actions.onConnectHealth, actions.onRefreshSleep)
            }

            StatsView.Phone -> {
                val panel = remember(model) { PhonePanels.build(model.phoneDays, data.today) }
                if (panel != null) BarPanelCard(panel)
                StatsPhoneCard(model.phoneToday, actions.onAllowUsage)
            }
        }
    }
}

// What is missing, in order of what can be done about it: link a device, wait for a sync, wait for the PC's goal, or simply no focus yet.
private fun emptyBody(model: StatsPaneModel, needsGoal: Boolean): String = when {
    !model.hasDevices -> "This phone isn't linked to your PC yet. Link it in Settings › Devices & sync and your focus time shows up here."
    !model.data.hasHistory -> "No focus sessions have arrived yet. They show up here after your PC's next sync."
    needsGoal && model.data.goal == null -> "Your PC hasn't sent its daily goal yet, so there is nothing to compare the days to. It arrives with the next sync."
    else -> "No deep work counted in this range yet."
}
