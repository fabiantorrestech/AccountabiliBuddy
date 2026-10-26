package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.lunch.LunchStore

/**
 * Settings → Your day → "Lunch length": how long a lunch started on this phone (or its watch) lasts, 15 minutes to
 * 2 hours in steps of 5. This phone's own setting; a flexible break's length in the day plan doesn't change it.
 */
@Composable
internal fun LunchSection(lengthMinutes: Int) {
    val lunch = (LocalContext.current.applicationContext as CompanionApp).lunch
    val length = LunchStore.formatLength(lengthMinutes)
    Section("Lunch length", summary = length) {
        Text(
            "On any workday, Start lunch on Today or the watch takes this long; while it runs, +5 / −5 min change it.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            OutlinedButton(
                onClick = { lunch.setLength(lengthMinutes - LunchStore.LENGTH_STEP) },
                enabled = lengthMinutes > LunchStore.MIN_LENGTH_MINUTES,
            ) { Text("−5 min") }
            Text(length, style = MaterialTheme.typography.titleMedium)
            OutlinedButton(
                onClick = { lunch.setLength(lengthMinutes + LunchStore.LENGTH_STEP) },
                enabled = lengthMinutes < LunchStore.MAX_LENGTH_MINUTES,
            ) { Text("+5 min") }
        }
    }
}
