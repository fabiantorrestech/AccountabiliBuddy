package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * When turned-off blocks come back on, per kind of day (ADR 37): Workdays and Off-Days, midnight unless changed. Each
 * opens a time wheel; the change is a day plan edit and goes to the PC. One-off days keep their own ("Plan a day").
 */
@Composable
internal fun ReenableTimes(onSet: (Boolean, LocalTime) -> String?) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val plan = app.store.state.collectAsState().value.document.dayPlan
    var editing by remember { mutableStateOf<Boolean?>(null) }
    var error by remember { mutableStateOf<String?>(null) }

    Column {
        Text("Blocks come back on at", style = MaterialTheme.typography.labelLarge, modifier = Modifier.padding(top = 4.dp))
        listOf(false to "Workdays", true to "Off-Days").forEach { (offDay, name) ->
            val time = (if (offDay) plan.otherDay else plan.workday).rules.reenableAt
            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier.fillMaxWidth().clickable { editing = offDay }.padding(vertical = 8.dp),
            ) {
                Text(name, modifier = Modifier.weight(1f))
                Text(timeText(time), color = MaterialTheme.colorScheme.primary)
            }
        }
        error?.let { Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall) }
    }

    editing?.let { offDay ->
        var picked by remember(offDay) { mutableStateOf((if (offDay) plan.otherDay else plan.workday).rules.reenableAt) }
        AlertDialog(
            onDismissRequest = { editing = null },
            title = { Text("${if (offDay) "Off-Days" else "Workdays"}: blocks back on at") },
            text = {
                Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.fillMaxWidth()) {
                    TimeWheel(picked, { picked = it })
                    Text("12:00 AM = midnight, the end of the day.", style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 8.dp))
                }
            },
            confirmButton = {
                TextButton(onClick = { error = onSet(offDay, picked); editing = null }) { Text("Save") }
            },
            dismissButton = { TextButton(onClick = { editing = null }) { Text("Cancel") } },
        )
    }
}

private val format = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

private fun timeText(time: LocalTime) = if (time == LocalTime.MIDNIGHT) "Midnight" else time.format(format)
