package com.accountabilibuddy.companion.ui

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.noise.NoisePlayer
import com.accountabilibuddy.companion.noise.NoiseSettings
import com.accountabilibuddy.companion.noise.NoiseState

/**
 * The noise player above the tabs, as in the PC's sidebar: the sound's name, previous, play/pause, next and the player's
 * own volume. Until a folder is chosen, one button to choose it.
 */
@Composable
fun NoiseBar(state: NoiseState, settings: NoiseSettings, player: NoisePlayer, onMessage: (String) -> Unit) {
    val pickFolder = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
        if (uri != null && !player.chooseFolder(uri)) onMessage("That folder can't be kept: choose a folder on the phone's storage.")
    }
    Surface(tonalElevation = 2.dp, modifier = Modifier.fillMaxWidth()) {
        if (!state.hasFolder) {
            TextButton(onClick = { pickFolder.launch(null) }, modifier = Modifier.padding(horizontal = 8.dp)) { Text("♪ Choose a folder of sounds") }
            return@Surface
        }

        var volume by remember(settings.volume) { mutableFloatStateOf(settings.volume.toFloat()) }
        Column(Modifier.padding(horizontal = 12.dp, vertical = 2.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    state.title ?: if (state.fileCount == 0) "No sounds in that folder" else "Noise",
                    style = MaterialTheme.typography.bodyMedium, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f),
                )
                TextButton(onClick = player::previous) { Text("⏮︎", fontSize = 18.sp) }
                TextButton(onClick = player::toggle) { Text(if (state.playing) "⏸︎" else "▶︎", fontSize = 18.sp) }
                TextButton(onClick = player::next) { Text("⏭︎", fontSize = 18.sp) }
            }
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("🔉︎", fontSize = 12.sp)
                Slider(
                    value = volume, onValueChange = { volume = it }, onValueChangeFinished = { player.setVolume(volume.toInt()) },
                    valueRange = 0f..100f, modifier = Modifier.weight(1f),
                )
                Text("${volume.toInt()}%", style = MaterialTheme.typography.bodySmall, modifier = Modifier.width(36.dp))
            }
        }
    }
}
