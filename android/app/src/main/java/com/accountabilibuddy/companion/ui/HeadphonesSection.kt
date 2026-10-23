package com.accountabilibuddy.companion.ui

import android.Manifest
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Checkbox
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.noise.HeadphoneWatcher

/** Settings → "Play when headphones connect": wired, and the chosen paired Bluetooth headphones. */
@Composable
internal fun HeadphonesSection() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val settings by app.headphones.settings.collectAsState()
    var paired by remember { mutableStateOf(HeadphoneWatcher.pairedAudio(context)) }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        paired = HeadphoneWatcher.pairedAudio(context)
        if (granted) {
            app.headphones.save(app.headphones.settings.value.copy(bluetooth = true))
        }
    }
    val needsPermission = Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && paired.isEmpty()
    val summary = listOfNotNull(
        "wired".takeIf { settings.wired },
        settings.devices.takeIf { settings.bluetooth && it.isNotEmpty() }?.joinToString { it.name },
    ).joinToString(" · ").ifEmpty { "Off" }

    Section("Play when headphones connect", summary = summary) {
        Text(
            "The noise player starts its last file when these headphones connect, any time of day, and pauses when they disconnect.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Text(
            "Tip: it really helps to have a dedicated pair of work/focus headphones, so putting them on means focus " +
                "and it doesn't get in the way of your normal headphones.",
            style = MaterialTheme.typography.bodySmall,
            modifier = Modifier.fillMaxWidth().background(Color(0x33F59E0B), RoundedCornerShape(8.dp)).padding(10.dp),
        )
        ToggleRow("Wired headphones", "Headphones or a headset in the jack or USB-C", settings.wired) {
            app.headphones.save(settings.copy(wired = it))
        }
        ToggleRow("Bluetooth headphones", "Only the ones ticked below", settings.bluetooth) { on ->
            if (on && needsPermission) {
                permission.launch(Manifest.permission.BLUETOOTH_CONNECT)
            } else {
                app.headphones.save(settings.copy(bluetooth = on))
                paired = HeadphoneWatcher.pairedAudio(context)
            }
        }
        if (settings.bluetooth) {
            val all = paired + settings.devices.filter { chosen -> paired.none { it.address == chosen.address } }
            all.forEach { device ->
                val ticked = settings.devices.any { it.address == device.address }
                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                    Checkbox(checked = ticked, onCheckedChange = { on ->
                        val others = settings.devices.filter { it.address != device.address }
                        app.headphones.save(settings.copy(devices = if (on) others + device else others))
                    })
                    Text(device.name, style = MaterialTheme.typography.bodyMedium)
                }
            }
            when {
                all.isEmpty() -> Text(
                    "No paired Bluetooth headphones. Pair them in Android's Bluetooth settings, or allow \"Nearby devices\".",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                settings.devices.isEmpty() -> Text(
                    "Tick the headphones that should start the noise.",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            OutlinedButton(onClick = {
                if (needsPermission) permission.launch(Manifest.permission.BLUETOOTH_CONNECT) else paired = HeadphoneWatcher.pairedAudio(context)
            }) { Text("Look again") }
        }
    }
}
