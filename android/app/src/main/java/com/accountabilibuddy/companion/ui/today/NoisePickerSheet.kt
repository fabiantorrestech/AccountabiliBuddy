package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.VolumeUp
import androidx.compose.material.icons.outlined.Folder
import androidx.compose.material.icons.outlined.MusicNote
import androidx.compose.material3.Icon
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
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.noise.NoisePlayer
import com.accountabilibuddy.companion.noise.NoisePlaylist
import com.accountabilibuddy.companion.noise.NoiseSettings
import com.accountabilibuddy.companion.noise.NoiseState
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetCheck
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.sheets.AbSheetValue
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * Noise chip › Noise (brief §4.1, board Today-Phone-Noise): an 80% sheet with the sounds of the chosen folder as a radio list (✓ on
 * the current one), "Your sound files" with the count (opens Settings › Alerts & on-screen, where the folder is chosen), the player's
 * own volume as a horizontal slider with its %, and a footer pointing to the headphone rules and schedules. The phone has no built-in
 * sounds: the list is the folder's files ([NoisePlaylist]); the board's names are samples.
 */
@Composable
fun NoisePickerSheet(state: NoiseState, settings: NoiseSettings, player: NoisePlayer, onOpenNoiseSettings: () -> Unit, onDismiss: () -> Unit) {
    // Read again whenever the player publishes (a new folder, a new current sound).
    val sounds = remember(state) { player.sounds() }
    val current = remember(state) { player.currentIndex() }
    var volume by remember(settings.volume) { mutableFloatStateOf(settings.volume.toFloat()) }

    AbSheet(
        title = "Noise",
        subtitle = "Plays on this phone · the watch can pause it",
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        footer = {
            Text("Headphones and schedules: Settings › Alerts & on-screen › Noise player", style = Ab.type.small, color = Ab.colors.muted)
        },
    ) {
        sounds.forEachIndexed { index, file ->
            val chosen = index == current
            AbSheetRow(
                title = NoisePlaylist.displayName(file.name),
                icon = Icons.Outlined.MusicNote,
                role = Role.RadioButton,
                modifier = Modifier.semantics { selected = chosen },
                onClick = { player.choose(index) },
                trailing = { AbSheetCheck(chosen) },
            )
        }
        AbSheetRow(
            title = if (state.hasFolder) "Your sound files" else "Choose a sound folder",
            subtitle = when {
                !state.hasFolder -> "In Settings › Alerts & on-screen"
                sounds.size == 1 -> "1 file"
                else -> "${sounds.size} files"
            },
            icon = Icons.Outlined.Folder,
            onClick = {
                onDismiss()
                onOpenNoiseSettings()
            },
            trailing = { AbSheetValue(null) },
        )
        Row(
            Modifier.fillMaxWidth().heightIn(min = 58.dp).padding(horizontal = 24.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Icon(Icons.AutoMirrored.Outlined.VolumeUp, contentDescription = null, tint = Ab.colors.muted, modifier = Modifier.size(22.dp))
            Slider(
                value = volume, onValueChange = { volume = it }, onValueChangeFinished = { player.setVolume(volume.toInt()) },
                valueRange = 0f..100f,
                modifier = Modifier.weight(1f).semantics { contentDescription = "Noise volume" },
                colors = SliderDefaults.colors(thumbColor = Ab.colors.accent, activeTrackColor = Ab.colors.accent, inactiveTrackColor = Ab.colors.surface2),
            )
            Text(
                "${volume.toInt()}%", style = Ab.type.bodyMedium.copy(fontFeatureSettings = "tnum"), color = Ab.colors.ink,
                textAlign = TextAlign.End, modifier = Modifier.widthIn(min = 44.dp),
            )
        }
    }
}
