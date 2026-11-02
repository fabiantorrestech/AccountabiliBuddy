package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import com.accountabilibuddy.companion.sync.PhoneSyncSettings

/**
 * Settings → Sleep → Wake-up alarm (it was on Devices): the phone's next alarm as the wake time (ADR 23), here and on the PCs,
 * and which app's alarms count (learned, calendar apps never count, or pinned).
 */
@Composable
internal fun WakeAlarmRows(
    settings: PhoneSyncSettings,
    alarmApps: List<Pair<String, String>>,
    learnedAlarmApp: String?,
    onSettings: (PhoneSyncSettings) -> Unit,
) {
    ToggleRow(
        "Next alarm as wake time",
        "Your next alarm (Clock, QR Alarm…) is your wake time here and on your PCs, which get it in this phone's sync file. It wins over a one-night change.",
        settings.alarmOn,
    ) { onSettings(settings.copy(alarmOn = it)) }
    if (settings.alarmOn) {
        AlarmAppPicker(settings.alarmApp, alarmApps, learnedAlarmApp) { app -> onSettings(settings.copy(alarmApp = app)) }
    }
}

@Composable
private fun AlarmAppPicker(pinned: String?, alarmApps: List<Pair<String, String>>, learnedAlarmApp: String?, onPick: (String?) -> Unit) {
    var open by remember { mutableStateOf(false) }
    fun name(app: String?) = alarmApps.firstOrNull { it.first == app }?.second ?: app ?: "none yet"
    val label = if (pinned != null) "Wake-up alarm: ${name(pinned)} (pinned)"
    else "Wake-up alarm: Auto · ${learnedAlarmApp?.let(::name) ?: "learning (any alarm but calendars)"}"
    Column {
        TextButton(onClick = { open = true }) { Text("$label ▾") }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            DropdownMenuItem(text = { Text("Auto (learn from my mornings)") }, onClick = { open = false; onPick(null) })
            alarmApps.filter { !it.first.contains("calendar", ignoreCase = true) }.forEach { (app, appName) ->
                DropdownMenuItem(text = { Text(appName) }, onClick = { open = false; onPick(app) })
            }
        }
    }
}
