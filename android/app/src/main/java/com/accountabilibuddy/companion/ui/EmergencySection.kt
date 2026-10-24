package com.accountabilibuddy.companion.ui

import android.content.Intent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Checkbox
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import java.time.LocalDate

/**
 * Settings → Emergency phone time: the daily allowance for "I need my phone for a bit" and the always-allowed apps
 * (unlocking into one never brings up the work card or counts as a pickup).
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun EmergencySection() {
    val context = LocalContext.current
    val store = (context.applicationContext as CompanionApp).emergency
    var daily by remember { mutableStateOf(store.dailyMinutes) }
    var allowed by remember { mutableStateOf(store.allowedApps) }
    var showAll by remember { mutableStateOf(false) }
    // The launchable apps, named; the always-allowed ones first.
    val apps = remember {
        val pm = context.packageManager
        pm.queryIntentActivities(Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER), 0)
            .map { it.activityInfo.packageName to it.loadLabel(pm).toString() }
            .filter { it.first != context.packageName }
            .distinctBy { it.first }
            .sortedBy { it.second.lowercase() }
    }
    val left = store.minutesLeft(LocalDate.now())

    Section("Emergency phone time", summary = "$daily min a day · ${allowed.count { id -> apps.any { it.first == id } }} always-allowed apps") {
        Text("\"I need my phone for a bit\" (in Not working today, and on the parked notice): a reason and 5–60 minutes without the work card or nudges, shown on the PC apart from pickups. $left min left today.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text("Minutes a day", style = MaterialTheme.typography.labelLarge)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf(15, 30, 45, 60, 90).forEach { minutes ->
                FilterChip(selected = daily == minutes, onClick = { store.dailyMinutes = minutes; daily = minutes }, label = { Text("$minutes") })
            }
        }
        Text("Always-allowed apps", style = MaterialTheme.typography.labelLarge)
        Text("Unlocking into one never brings up the work card and isn't a pickup: calls, messages, directions, authenticators, banking.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        val shown = if (showAll) apps else apps.filter { it.first in allowed }
        shown.forEach { (id, name) ->
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                Checkbox(checked = id in allowed, onCheckedChange = { on ->
                    allowed = if (on) allowed + id else allowed - id
                    store.allowedApps = allowed
                })
                Text(name, style = MaterialTheme.typography.bodyMedium)
            }
        }
        TextButton(onClick = { showAll = !showAll }) { Text(if (showAll) "Show only the allowed ones" else "Add apps…") }
    }
}
