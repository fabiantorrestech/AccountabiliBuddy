package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp

/** The migration assistant's page for day plan modes (ADR 47, since 0.1.62): what changed in the plan and where to find it. */
@Composable
internal fun DayPlanModesSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Day plan modes", style = MaterialTheme.typography.headlineSmall)
        Text("Your plan now has day types, a week map and three modes. Nothing changes until you edit it: your Workday and Off day are the first two day types.")
        Text("Edit plan (on Today) → Day types:", style = MaterialTheme.typography.titleSmall)
        Text("• Schedule: the timed parts you had, or just a work window (say 9:00 AM–5:00 PM).")
        Text("• Budget: a number of focus hours; the buddy guards the day from wake to bedtime minus the rest windows you list, until the hours are counted.")
        Text("• Free: no guard that day.")
        Text("Edit plan → Week:", style = MaterialTheme.typography.titleSmall)
        Text("• Pick a day type for each weekday, and turn on alternating weeks to give week B its own. Week B starts on the Monday you choose, and \"This week is A/B\" on Today switches it.")
        Text(
            "Changes go to the PC like any plan edit: ones that add work apply at once, ones that free today's work wait there.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
