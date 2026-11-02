package com.accountabilibuddy.companion.ui.plan

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.ThisWeekCard
import com.accountabilibuddy.companion.ui.kit.AbPageHeader
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The Plan tab (board 5c): "Plan" with the picked day's name and a ⋮ menu, the Monday-to-Sunday strip with each day's type,
 * the notes about that day, its timeline, and the two buttons "Change a day" and "Templates" pinned at the bottom. It only
 * draws what [model] holds and forwards taps through [actions]; the host owns the state and the saving, and nothing here
 * starts, ends or unlocks anything. It has no Scaffold and no fixed width, and the timeline and the notes are separate
 * composables ([PlanTimeline], [PlanDayDetails]), so a tablet can place them side by side later.
 *
 * With no PC to take the edit ([PlanPaneModel.canChange] false) the page is read-only: the two buttons are there but off, and
 * the notes say why.
 */
@Composable
fun PlanPane(model: PlanPaneModel, actions: PlanPaneActions, modifier: Modifier = Modifier) {
    var weekSheet by remember { mutableStateOf(false) }
    Column(modifier.fillMaxSize()) {
        LazyColumn(
            Modifier.weight(1f),
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 12.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item {
                Column {
                    AbPageHeader("Plan") { PlanMenu(model, actions, onWeekSheet = { weekSheet = true }) }
                    Text(
                        model.title + (model.weekLabel?.let { " · $it" } ?: ""),
                        style = Ab.type.bodyMedium, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 4.dp),
                    )
                }
            }
            item { WeekStrip(model.cells, onPick = actions.onPick, onShiftWeek = actions.onShiftWeek) }
            item { PlanDayDetails(model, actions) }
            if (model.timeline.rows.isEmpty()) {
                item { Text(model.emptyText ?: "Nothing is planned for this day.", style = Ab.type.bodyMedium, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 4.dp)) }
            } else {
                item { PlanTimeline(model.timeline.rows) }
            }
        }
        Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, bottom = 12.dp, top = 4.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            AbTonalButton("Change a day", onClick = actions.onChangeDay, enabled = model.canChange, modifier = Modifier.weight(1f))
            AbTonalButton("Templates", onClick = actions.onTemplates, enabled = model.canChange, modifier = Modifier.weight(1f))
        }
    }

    if (weekSheet) {
        AbSheet(onDismiss = { weekSheet = false }) {
            ThisWeekCard(model.weekNumber, model.weekCount) { week ->
                actions.onSetWeek(week)
                weekSheet = false
            }
        }
    }
}
