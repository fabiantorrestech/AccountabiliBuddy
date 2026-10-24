package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.NightKind

/**
 * Settings → Smart bedtime (ADR 34): a switch per kind of night, the minimum and maximum sleep, and what it learned.
 * It learns from Health Connect sleep and the phone's last use before it, then picks tonight's bedtime from tomorrow's
 * wake time, 15 minutes earlier after each week made on most nights.
 */
@Composable
internal fun SmartBedtimeSection(clock: ClockFormat) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val service = app.smartBedtime
    val store = service.store
    // Bumped on every change, so the section reads the store again.
    var version by remember { mutableIntStateOf(0) }
    fun changed() {
        version++
        service.refresh()
        app.refreshSoon()
    }

    val on = NightKind.entries.filter { store.isOn(it) }
    Section("Smart bedtime", summary = if (on.isEmpty()) "Off" else on.joinToString(" · ") { label(it) }) {
        version.let { } // read so changes redraw
        Text(
            "Learns how long you really sleep (Health Connect) and when you put the phone down, then sets tonight's bedtime from tomorrow's wake time. It starts from what you do now and moves 15 minutes earlier after each week you made it on most nights: first to your minimum, then toward your goal.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        NightKind.entries.forEach { kind ->
            ToggleRow(label(kind), service.status(kind, clock), store.isOn(kind)) { value ->
                store.setOn(kind, value)
                changed()
            }
        }
        Text("Minimum sleep", style = MaterialTheme.typography.labelLarge)
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            listOf(5.5, 6.0, 6.5, 7.0, 7.5).forEach { hours ->
                FilterChip(selected = store.minHours == hours, onClick = { store.minHours = hours; changed() }, label = { Text(hoursText(hours)) })
            }
        }
        Text("Maximum sleep", style = MaterialTheme.typography.labelLarge)
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            listOf(8.0, 8.5, 9.0, 9.5, 10.0).forEach { hours ->
                FilterChip(selected = store.maxHours == hours, onClick = { store.maxHours = hours; changed() }, label = { Text(hoursText(hours)) })
            }
        }
    }
}

private fun label(kind: NightKind): String = if (kind == NightKind.Workday) "Workdays" else "Off-Days"

private fun hoursText(hours: Double): String = if (hours % 1.0 == 0.0) "${hours.toInt()}h" else "${hours.toInt()}h 30"
