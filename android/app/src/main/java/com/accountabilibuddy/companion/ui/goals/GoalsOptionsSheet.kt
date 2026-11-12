package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.List
import androidx.compose.material.icons.automirrored.outlined.Sort
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Home
import androidx.compose.material.icons.outlined.Insights
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material.icons.outlined.Visibility
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.goals.GoalDisplay
import com.accountabilibuddy.companion.goals.GoalHeatmapLabels
import com.accountabilibuddy.companion.goals.GoalListView
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.kit.AbSwitch
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetCheck
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.sheets.AbSheetValue
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalWeekStart

/**
 * Goals › Options (brief §4.3, board Goals-Phone-Options; 80% sheet), the same rows as the PC's Options menu:
 * - LIST: Show goals (By area / Current / All, inline), Sort ("Your order" ›, its own page), Areas (rename, pick icons, reorder,
 *   add › R6b).
 * - CHARTS: Week starts on (Mon / Sun / Sat; Sun by default), Heatmap day labels (All / Some / None), Extra tiles on goal pages
 *   (Streak, Total; off: they live in Stats; a goal's own switch in Edit goal wins).
 * - MORE: Goals settings › (last, as every page's Options ends with its settings row, brief §3.3).
 * Every choice is this phone's own and applies at once.
 */
@Composable
fun GoalsOptionsSheet(
    display: GoalDisplay,
    areaCount: Int,
    onView: (GoalListView) -> Unit,
    onWeekStart: (GoalWeekStart) -> Unit,
    onHeatmapLabels: (GoalHeatmapLabels) -> Unit,
    onExtraTiles: (Boolean) -> Unit,
    onOpenAreas: () -> Unit,
    onOpenGoalsSettings: () -> Unit,
    onDismiss: () -> Unit,
) {
    var sortOpen by rememberSaveable { mutableStateOf(false) }
    if (sortOpen) {
        AbSheet(title = "Sort", subtitle = "Goals", onDismiss = onDismiss, height = AbSheetHeight.Tall, onBack = { sortOpen = false }) {
            // Only the user's own order exists today (areas, then goals, as placed); other orders would come with R6b's editors.
            AbSheetRow("Your order", onClick = { sortOpen = false }, subtitle = "Areas and goals as you placed them", role = Role.RadioButton, trailing = { AbSheetCheck(true) })
        }
        return
    }

    AbSheet(title = "Options", subtitle = "Goals", onDismiss = onDismiss, height = AbSheetHeight.Tall) {
        Head("LIST")
        AbSheetRow("Show goals", onClick = null, icon = Icons.AutoMirrored.Outlined.List, trailing = {
            val views = GoalListView.entries
            AbSegmented(views.map { it.label }, views.indexOf(display.view), { onView(views[it]) }, Modifier.width(200.dp))
        })
        AbSheetRow("Sort", onClick = { sortOpen = true }, icon = Icons.AutoMirrored.Outlined.Sort, trailing = { AbSheetValue("Your order") })
        AbSheetRow("Areas", onClick = onOpenAreas, icon = Icons.Outlined.Home, subtitle = "Rename, pick icons, reorder, add", trailing = { AbSheetValue(areaCount.toString()) })

        Head("CHARTS")
        AbSheetRow("Week starts on", onClick = null, icon = Icons.Outlined.CalendarMonth, trailing = {
            val starts = listOf(GoalWeekStart.Monday, GoalWeekStart.Sunday, GoalWeekStart.Saturday)
            AbSegmented(listOf("Mon", "Sun", "Sat"), starts.indexOf(display.weekStart), { onWeekStart(starts[it]) }, Modifier.width(160.dp))
        })
        AbSheetRow("Heatmap day labels", onClick = null, icon = Icons.Outlined.Insights, trailing = {
            val labels = GoalHeatmapLabels.entries
            AbSegmented(labels.map { it.label }, labels.indexOf(display.heatmapLabels), { onHeatmapLabels(labels[it]) }, Modifier.width(160.dp))
        })
        AbSheetRow(
            "Extra tiles on goal pages", onClick = { onExtraTiles(!display.extraTiles) }, icon = Icons.Outlined.Visibility,
            subtitle = "Streak, Total · otherwise in Stats", role = Role.Switch,
            trailing = { AbSwitch(display.extraTiles, onCheckedChange = onExtraTiles) },
        )

        Head("MORE")
        AbSheetRow("Goals settings", onClick = onOpenGoalsSettings, icon = Icons.Outlined.Settings, subtitle = "Weekly review, reminders, Beeminder, pausing", trailing = { AbSheetValue(null) })
    }
}

@Composable
private fun Head(text: String) {
    Text(text, style = Ab.type.micro, fontWeight = FontWeight.SemiBold, color = Ab.colors.muted, modifier = Modifier.padding(start = 24.dp, top = 12.dp, bottom = 4.dp))
}
