package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import java.time.LocalDate

/** Settings → Emergency phone time: the daily allowance for "I need my phone for a bit" (the apps are in Essentials). */
@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun EmergencySection() {
    val context = LocalContext.current
    val store = (context.applicationContext as CompanionApp).emergency
    var daily by remember { mutableStateOf(store.dailyMinutes) }
    val left = store.minutesLeft(LocalDate.now())

    Section("Emergency phone time", summary = "$daily min a day · $left left today") {
        Text("\"I need my phone for a bit\" (in Not working today, and on the parked notice): a reason and 5–60 minutes without the work card or nudges, then the essentials screen; shown on the PC apart from pickups.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text("Minutes a day", style = MaterialTheme.typography.labelLarge)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf(15, 30, 45, 60, 90).forEach { minutes ->
                FilterChip(selected = daily == minutes, onClick = { store.dailyMinutes = minutes; daily = minutes }, label = { Text("$minutes") })
            }
        }
    }
}
