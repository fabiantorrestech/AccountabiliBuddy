package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Bedtime
import androidx.compose.material.icons.outlined.CalendarViewMonth
import androidx.compose.material.icons.outlined.Dashboard
import androidx.compose.material.icons.outlined.DirectionsWalk
import androidx.compose.material.icons.outlined.GridOn
import androidx.compose.material.icons.outlined.ShowChart
import androidx.compose.material.icons.outlined.Smartphone
import androidx.compose.material.icons.outlined.Timeline
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.stats.StatsView
import com.accountabilibuddy.companion.ui.SectionIcons
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow

/**
 * The views as a row of chips that scrolls sideways (boards 19e to 19g). The selected chip is ink on the second tone, never the
 * accent. [views] is the order the person chose, hidden ones left out; the one on show is always there.
 */
@Composable
fun StatsViewChips(views: List<StatsView>, selected: StatsView, onSelect: (StatsView) -> Unit, modifier: Modifier = Modifier) {
    AbChipRow(modifier, contentPadding = PaddingValues(vertical = 2.dp)) {
        views.forEach { view -> AbChip(view.title, selected = view == selected, onClick = { onSelect(view) }) }
    }
}

/** The same icon for a view on the PC and the phone (the PC's Stats chips and the 19h sheet). */
fun StatsView.icon(): ImageVector = when (this) {
    StatsView.Overview -> Icons.Outlined.Dashboard
    StatsView.Consistency -> Icons.Outlined.CalendarViewMonth
    StatsView.Intensity -> Icons.Outlined.GridOn
    StatsView.FocusGraph -> Icons.Outlined.ShowChart
    StatsView.Drift -> Icons.Outlined.Timeline
    StatsView.Sleep -> Icons.Outlined.Bedtime
    StatsView.Phone -> Icons.Outlined.Smartphone
    StatsView.Steps -> Icons.Outlined.DirectionsWalk
    StatsView.Goals -> SectionIcons.Goals
}
