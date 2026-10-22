package com.accountabilibuddy.watch.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.wear.compose.material3.OutlinedButton
import androidx.wear.compose.material3.Text
import com.accountabilibuddy.watch.alerts.AlertSettings
import com.accountabilibuddy.watch.alerts.Vibration

/** How the watch alerts when a focus or break ends: tap each to change it. [onChanged] sets the alerts again. */
@Composable
fun AlertsPage(onChanged: () -> Unit) {
    val context = LocalContext.current
    var vibration by remember { mutableStateOf(AlertSettings.vibration(context)) }
    var sound by remember { mutableStateOf(AlertSettings.sound(context)) }
    var warning by remember { mutableStateOf(AlertSettings.warning(context)) }
    Column(
        Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(6.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("When a timer ends", fontSize = 13.sp)
        OutlinedButton(onClick = {
            vibration = Vibration.entries[(vibration.ordinal + 1) % Vibration.entries.size]
            AlertSettings.setVibration(context, vibration)
            onChanged()
        }) { Text("Vibration: ${vibration.label}") }
        OutlinedButton(onClick = {
            sound = !sound
            AlertSettings.setSound(context, sound)
            onChanged()
        }) { Text("Sound: ${if (sound) "On" else "Off"}") }
        OutlinedButton(onClick = {
            warning = !warning
            AlertSettings.setWarning(context, warning)
            onChanged()
        }) { Text("1 min before: ${if (warning) "On" else "Off"}") }
    }
}
