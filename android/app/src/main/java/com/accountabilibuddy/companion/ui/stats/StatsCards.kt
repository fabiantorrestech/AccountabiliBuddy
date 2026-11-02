package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.stats.BarPanel
import com.accountabilibuddy.companion.stats.HeatPanel
import com.accountabilibuddy.companion.stats.StatsDayRow
import com.accountabilibuddy.companion.stats.StatsView
import com.accountabilibuddy.companion.stats.TrendPanel
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbListRow
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.LocalDate

/** The big number at the top of a chart card ("8h 52m"): the theme's numeral font. */
@Composable
internal fun bigNumber(): TextStyle = Ab.type.timerSmall.copy(fontSize = 34.sp, lineHeight = 38.sp)

/** A big number with what it counts, a bar chart, the tiles and an honest line about the depth of the data (Overview, Drift, Sleep, Phone). */
@Composable
fun BarPanelCard(
    panel: BarPanel,
    modifier: Modifier = Modifier,
    onBarClick: ((LocalDate) -> Unit)? = null,
    onOpenView: ((StatsView) -> Unit)? = null,
) {
    AbCard(modifier, verticalSpacing = 14.dp) {
        Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Text(panel.big, style = bigNumber(), color = Ab.colors.ink)
            Text(panel.note, style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.weight(1f).padding(bottom = 4.dp))
        }

        BarChart(panel.bars, panel.goalLine, panel.goalLabel, onBarClick = onBarClick)
        StatTiles(panel.cards, onOpenView = onOpenView)
        panel.caption?.let { Text(it, style = Ab.type.micro, color = Ab.colors.muted) }
    }
}

/** Consistency (board 19e) or Intensity (board 19f): the title and what it shows, the heatmap with its key, the tiles and the advice. */
@Composable
fun HeatPanelCard(
    panel: HeatPanel,
    modifier: Modifier = Modifier,
    onDayClick: ((LocalDate) -> Unit)? = null,
) {
    AbCard(modifier, verticalSpacing = 14.dp) {
        Column {
            Text(panel.title, style = Ab.type.section, color = Ab.colors.ink)
            Text(panel.subtitle, style = Ab.type.bodyMedium, color = Ab.colors.muted)
        }

        if (panel.rowLabels == null) DayHeatmap(panel, onDayClick = onDayClick) else HourHeatmap(panel)
        HeatLegend()
        panel.helper?.let { Text(it, style = Ab.type.bodyMedium, color = Ab.colors.ink) }
        StatTiles(panel.cards)
        Text(panel.note, style = Ab.type.micro, color = Ab.colors.muted)
        panel.caption?.let { Text(it, style = Ab.type.micro, color = Ab.colors.muted) }
    }
}

/** The Focus graph card (board 19g). */
@Composable
fun TrendPanelCard(panel: TrendPanel, modifier: Modifier = Modifier) {
    AbCard(modifier, verticalSpacing = 14.dp) {
        Column {
            Text("Focus graph", style = Ab.type.section, color = Ab.colors.ink)
            Text(panel.subtitle, style = Ab.type.bodyMedium, color = Ab.colors.muted)
        }

        FocusGraph(panel)
        FocusGraphLegend(showDrift = panel.drift != null)
        StatTiles(panel.cards)
        panel.caption?.let { Text(it, style = Ab.type.micro, color = Ab.colors.muted) }
    }
}

/** The days under the Overview chart (board 5d); tapping one opens it. */
@Composable
fun StatsDaysCard(rows: List<StatsDayRow>, onDayClick: (LocalDate) -> Unit, modifier: Modifier = Modifier) {
    if (rows.isEmpty()) return
    AbCard(modifier, contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
        rows.forEachIndexed { index, row ->
            AbListRow(row.title, subtitle = row.detail, onClick = { onDayClick(row.date) }, divider = index < rows.lastIndex)
        }
    }
}

/** An honest empty state: what is missing and how it gets there. */
@Composable
fun StatsEmptyCard(title: String, body: String, modifier: Modifier = Modifier) {
    AbCard(modifier) {
        Text(title, style = Ab.type.section, color = Ab.colors.ink)
        Text(body, style = Ab.type.bodyMedium, color = Ab.colors.muted, modifier = Modifier.fillMaxWidth())
    }
}
