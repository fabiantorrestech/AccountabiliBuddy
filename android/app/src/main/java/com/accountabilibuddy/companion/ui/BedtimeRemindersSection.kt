package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
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
import com.accountabilibuddy.companion.model.BedtimeReminder
import com.accountabilibuddy.companion.model.BedtimeReminderSettings

/**
 * Settings → Sleep → Bedtime reminders (ADR 38): on/off and the list (shared with the PCs): each at some minutes before
 * bedtime, a notification or an alarm (sound, volume, vibration), on the chosen devices. Tap one to change it.
 */
@OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
@Composable
internal fun BedtimeRemindersSection() {
    val app = LocalContext.current.applicationContext as CompanionApp
    val sleep = app.store.state.collectAsState().value.sleep
    val settings = sleep.reminders
    val received by app.sync.received.collectAsState()
    // The PCs the phone knows, for "this reminder on that PC".
    val pcs = received.filter { it.file.device.kind == com.accountabilibuddy.shared.sync.SyncDeviceCard.KIND_PC }
        .map { "pc:${it.file.device.deviceId}" to it.file.device.name }
    var editing by remember { mutableStateOf<Int?>(null) }

    fun save(next: BedtimeReminderSettings) {
        app.sleepSync.saveEdit(sleep.copy(reminders = next))
        app.refreshSoon()
    }
    fun change(index: Int, edit: (BedtimeReminder) -> BedtimeReminder) =
        save(settings.copy(list = settings.list.mapIndexed { i, r -> if (i == index) edit(r) else r }))

    val ordered = settings.list.withIndex().sortedByDescending { it.value.minutesBefore }
    Section("Bedtime reminders", summary = if (!settings.on) "Off" else "${settings.list.size} reminders · ${settings.list.count { it.alarm }} ring") {
        ToggleRow("Bedtime reminders", "Before bedtime on your devices; a live countdown from the first one. Set here or on a PC.", settings.on) { save(settings.copy(on = it)) }
        if (!settings.on) return@Section
        ordered.forEachIndexed { position, (index, reminder) ->
            HorizontalDivider()
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.weight(1f)) {
                    TextButton(onClick = { editing = if (editing == index) null else index }) {
                        Text("${position + 1}/${ordered.size} · ${whenText(reminder.minutesBefore)} · ${if (reminder.alarm) "Alarm" else "Notification"}")
                    }
                    Text(devicesText(reminder.devices, pcs), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(start = 12.dp))
                }
                IconButton(onClick = { save(settings.copy(list = settings.list.filterIndexed { i, _ -> i != index })); editing = null }) {
                    Icon(Icons.Outlined.Close, contentDescription = "Remove this reminder")
                }
            }
            if (editing == index) {
                Column(Modifier.padding(start = 12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text("When", style = MaterialTheme.typography.labelLarge)
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        listOf(0, 5, 10, 15, 30, 45, 60, 90, 120).forEach { minutes ->
                            FilterChip(selected = reminder.minutesBefore == minutes, onClick = { change(index) { it.copy(minutesBefore = minutes) } },
                                label = { Text(whenText(minutes)) })
                        }
                    }
                    ToggleRow("Ring as an alarm", "Else a notification", reminder.alarm) { on -> change(index) { it.copy(alarm = on) } }
                    Text("On", style = MaterialTheme.typography.labelLarge)
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        (listOf(BedtimeReminder.PHONE to "Phone", BedtimeReminder.WATCH to "Watch", BedtimeReminder.ALL_PCS to "All PCs") + pcs).forEach { (token, name) ->
                            FilterChip(
                                selected = token in reminder.devices,
                                onClick = { change(index) { it.copy(devices = if (token in it.devices) it.devices - token else it.devices + token) } },
                                label = { Text(name) },
                            )
                        }
                    }
                    if (reminder.alarm) {
                        Text("Sound", style = MaterialTheme.typography.labelLarge)
                        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            listOf("bell" to "Alarm", "chime" to "Chime", "rising" to "Ringtone").forEach { (sound, name) ->
                                FilterChip(selected = reminder.sound == sound, onClick = { change(index) { it.copy(sound = sound) } }, label = { Text(name) })
                            }
                        }
                        Text("Volume ${reminder.volume}%", style = MaterialTheme.typography.labelLarge)
                        var volume by remember(index, reminder.volume) { mutableStateOf(reminder.volume.toFloat()) }
                        Slider(value = volume, onValueChange = { volume = it }, valueRange = 0f..100f, steps = 9,
                            onValueChangeFinished = { change(index) { it.copy(volume = volume.toInt()) } })
                        ToggleRow("Vibrate", "With the sound", reminder.vibrate) { on -> change(index) { it.copy(vibrate = on) } }
                    }
                }
            }
        }
        HorizontalDivider()
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            TextButton(onClick = {
                val used = settings.list.map { it.minutesBefore }.toSet()
                val minutes = listOf(30, 60, 90, 120, 10, 5).firstOrNull { it !in used } ?: 20
                save(settings.copy(list = settings.list + BedtimeReminder(minutes, false, listOf(BedtimeReminder.PHONE))))
            }) { Text("Add a reminder") }
            TextButton(onClick = { save(BedtimeReminderSettings.Default) }) { Text("Reset to the 3 defaults") }
        }
    }
}

private fun whenText(minutes: Int): String = when {
    minutes == 0 -> "At bedtime"
    minutes % 60 == 0 -> "${minutes / 60} h before"
    minutes > 60 -> "${minutes / 60} h ${minutes % 60} min before"
    else -> "$minutes min before"
}

private fun devicesText(devices: List<String>, pcs: List<Pair<String, String>>): String = devices.map { token ->
    when (token) {
        BedtimeReminder.PHONE -> "Phone"
        BedtimeReminder.WATCH -> "Watch"
        BedtimeReminder.ALL_PCS -> "All PCs"
        else -> pcs.firstOrNull { it.first == token }?.second ?: "a PC"
    }
}.joinToString(", ").ifEmpty { "No device" }
