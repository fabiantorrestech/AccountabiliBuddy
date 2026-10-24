package com.accountabilibuddy.companion.nudge

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import java.time.LocalDate

/**
 * "I need my phone for a bit": a reason (Interview or call, Urgent message, Directions, or Other with a line) and a
 * length that fits what's left of today's allowance. [onStarted] after it starts; [onBack] to the previous choices.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun EmergencyChooser(store: EmergencyStore, onStarted: () -> Unit, onBack: () -> Unit, light: Boolean = true) {
    val left = remember { store.minutesLeft(LocalDate.now()) }
    var reason by remember { mutableStateOf<String?>(null) }
    var other by remember { mutableStateOf("") }
    var minutes by remember { mutableStateOf<Int?>(null) }
    val textColor = if (light) Color.White else MaterialTheme.colorScheme.onSurface
    val dim = if (light) Color(0xCCFFFFFF) else MaterialTheme.colorScheme.onSurfaceVariant
    Column(verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
        Text("I need my phone for a bit", color = textColor, style = MaterialTheme.typography.titleLarge)
        Text("$left of ${store.dailyMinutes} min left today. No work card or nudges meanwhile; shown on the PC apart from pickups.",
            color = dim, style = MaterialTheme.typography.bodyMedium)
        if (left <= 0) {
            Text("Today's emergency time is used up. A day off is still there.", color = Color(0xFFF4A261), style = MaterialTheme.typography.bodyLarge)
            TextButton(onClick = onBack) { Text("Back", color = dim) }
            return@Column
        }

        Text("Why?", color = dim, style = MaterialTheme.typography.labelLarge)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            (EmergencyStore.REASONS + "Other").forEach { option ->
                FilterChip(selected = reason == option, onClick = { reason = option }, label = { Text(option) })
            }
        }
        if (reason == "Other") {
            OutlinedTextField(value = other, onValueChange = { other = it }, label = { Text("What for?") }, singleLine = true, modifier = Modifier.fillMaxWidth())
        }
        Text("For how long?", color = dim, style = MaterialTheme.typography.labelLarge)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            EmergencyStore.LENGTHS.forEach { length ->
                FilterChip(selected = minutes == length, enabled = length <= left, onClick = { minutes = length }, label = { Text("$length min") })
            }
        }
        val why = if (reason == "Other") other.trim().takeIf { it.isNotEmpty() } else reason
        Button(
            enabled = why != null && minutes != null,
            onClick = { if (store.start(why!!, minutes!!)) onStarted() },
            modifier = Modifier.fillMaxWidth(),
        ) { Text(minutes?.let { "Use my phone for $it min" } ?: "Use my phone") }
        TextButton(onClick = onBack) { Text("Back", color = dim) }
    }
}
