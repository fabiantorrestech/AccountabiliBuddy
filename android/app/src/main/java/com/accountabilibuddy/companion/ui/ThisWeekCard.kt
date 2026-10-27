package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.Card
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

/**
 * Today, with the plan's weeks repeating in a cycle of more than one week (schema v13, ADR 47): "This is week N of M".
 * Picking another week is a day plan edit sent to the PC (it moves week 1's start by whole weeks); the PC applies it at
 * once when it adds work, and holds it when it frees today's work.
 */
@Composable
fun ThisWeekCard(weekNumber: Int, weekCount: Int, onPick: (week: Int) -> Unit) {
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text("This is week $weekNumber of $weekCount", style = MaterialTheme.typography.bodyLarge)
            Row(Modifier.horizontalScroll(rememberScrollState()), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                for (week in 1..weekCount) {
                    FilterChip(selected = week == weekNumber, onClick = { if (week != weekNumber) onPick(week) }, label = { Text("$week") })
                }
            }
            Text(
                "Sent to the PC like any plan edit; a switch that frees today's work waits there.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}
