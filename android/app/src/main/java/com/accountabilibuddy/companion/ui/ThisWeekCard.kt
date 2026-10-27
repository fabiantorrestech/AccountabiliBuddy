package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Card
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

/**
 * Today, with alternating weeks on (schema v12, ADR 47): "This week is A / B". Picking the other one is a day plan edit sent
 * to the PC (it moves week B's start); the PC applies it at once when it adds work, and holds it when it frees today's work.
 */
@Composable
fun ThisWeekCard(weekB: Boolean, onPick: (weekB: Boolean) -> Unit) {
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("This week is", style = MaterialTheme.typography.bodyLarge)
                FilterChip(selected = !weekB, onClick = { if (weekB) onPick(false) }, label = { Text("A") })
                FilterChip(selected = weekB, onClick = { if (!weekB) onPick(true) }, label = { Text("B") })
            }
            Text(
                "Sent to the PC like any plan edit; a switch that frees today's work waits there.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}
