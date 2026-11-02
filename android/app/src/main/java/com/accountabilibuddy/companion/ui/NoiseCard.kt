package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.LockOpen
import androidx.compose.material.icons.filled.PauseCircle
import androidx.compose.material.icons.filled.PlayCircle
import androidx.compose.material.icons.filled.SkipNext
import androidx.compose.material.icons.filled.SkipPrevious
import androidx.compose.material.icons.automirrored.outlined.VolumeUp
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.noise.NoisePlayer
import com.accountabilibuddy.companion.noise.NoiseSettings
import com.accountabilibuddy.companion.noise.NoiseState
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The noise player, as in the PC's sidebar: the sound's name, previous, play/pause, next and the player's own volume, and the
 * lock (plays only with headphones connected). Today shows it as a card once a sound folder is set; the folder is chosen in
 * Settings › Alerts & on-screen. Ink and tonal colours: the accent is for progress and the one main button.
 */
@Composable
fun NoiseCard(state: NoiseState, settings: NoiseSettings, player: NoisePlayer) {
    val colors = Ab.colors
    var volume by remember(settings.volume) { mutableFloatStateOf(settings.volume.toFloat()) }
    AbCard(contentPadding = PaddingValues(horizontal = 12.dp, vertical = 6.dp), verticalSpacing = 0.dp) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            if (state.needsHeadphones) {
                Text("Connect headphones", style = Ab.type.bodyMedium, color = colors.drift, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f).padding(start = 8.dp))
            } else {
                Text(
                    state.title ?: if (state.fileCount == 0) "No sounds in that folder" else "Noise",
                    style = Ab.type.bodyMedium, color = colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f).padding(start = 8.dp),
                )
            }
            // The lock: the drift colour while it keeps the player quiet (no headphones).
            IconButton(onClick = { player.setHeadphoneLock(!settings.headphoneLock) }) {
                Icon(
                    if (settings.headphoneLock) Icons.Filled.Lock else Icons.Filled.LockOpen,
                    contentDescription = if (settings.headphoneLock) "Locked to headphones (tap to unlock)" else "Lock to headphones",
                    tint = when {
                        !settings.headphoneLock -> colors.muted
                        state.headphonesConnected -> colors.ink
                        else -> colors.drift
                    },
                    modifier = Modifier.size(22.dp),
                )
            }
            IconButton(onClick = player::previous) { Icon(Icons.Filled.SkipPrevious, contentDescription = "Previous", tint = colors.muted, modifier = Modifier.size(28.dp)) }
            IconButton(onClick = player::next) { Icon(Icons.Filled.SkipNext, contentDescription = "Next", tint = colors.muted, modifier = Modifier.size(28.dp)) }
            IconButton(onClick = player::toggle, modifier = Modifier.size(56.dp)) {
                Icon(
                    if (state.playing) Icons.Filled.PauseCircle else Icons.Filled.PlayCircle,
                    contentDescription = if (state.playing) "Pause" else "Play",
                    tint = colors.ink,
                    modifier = Modifier.size(48.dp),
                )
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Icon(Icons.AutoMirrored.Outlined.VolumeUp, contentDescription = null, tint = colors.muted, modifier = Modifier.size(18.dp))
            Slider(
                value = volume, onValueChange = { volume = it }, onValueChangeFinished = { player.setVolume(volume.toInt()) },
                valueRange = 0f..100f, modifier = Modifier.weight(1f),
                colors = SliderDefaults.colors(thumbColor = colors.ink, activeTrackColor = colors.ink, inactiveTrackColor = colors.surface2),
            )
            Text("${volume.toInt()}%", style = Ab.type.small, color = colors.muted, modifier = Modifier.width(40.dp))
        }
    }
}

