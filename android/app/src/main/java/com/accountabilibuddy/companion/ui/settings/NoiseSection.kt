package com.accountabilibuddy.companion.ui.settings

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.noise.NoisePlayer
import com.accountabilibuddy.companion.noise.NoiseSettings
import com.accountabilibuddy.companion.sync.DriveFolderSource
import com.accountabilibuddy.companion.ui.Section
import com.accountabilibuddy.companion.ui.ToggleRow
import com.accountabilibuddy.companion.ui.kit.AbValueButton
import com.accountabilibuddy.companion.ui.theme.Ab

/** The noise player's folder and switches (Today's noise card plays it once a folder is set). */
@Composable
internal fun NoiseSection(player: NoisePlayer, settings: NoiseSettings) {
    val pickFolder = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri ->
        uri?.let(player::chooseFolder)
    }
    val folder = settings.folderUri?.let { DriveFolderSource.displayName(it) }
    Section("Noise player", summary = folder?.let { "Folder: $it" } ?: "No folder yet") {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(folder?.let { "Folder: $it" } ?: "No folder chosen", style = Ab.type.bodyMedium, color = Ab.colors.ink, modifier = Modifier.weight(1f))
            AbValueButton(if (folder == null) "Choose folder" else "Change", onClick = { pickFolder.launch(null) })
        }
        ToggleRow("Repeat", "Play the same sound again and again", settings.repeat, onChange = player::setRepeat)
        ToggleRow(
            "Lock to headphones",
            "Plays only while headphones are connected (wired or Bluetooth), and pauses when they go. Also the lock on the player.",
            settings.headphoneLock, onChange = player::setHeadphoneLock,
        )
        ToggleRow(
            "Stop when headphones disconnect",
            "Pauses the player when headphones go, locked or not. Separate from playing when they connect.",
            settings.stopOnDisconnect, onChange = player::setStopOnDisconnect,
        )
        ToggleRow(
            "Pause during calls",
            "Pauses the player while a call rings or is active, and resumes it afterwards if it was playing.",
            settings.pauseDuringCalls, onChange = player::setPauseDuringCalls,
        )
        ToggleRow(
            "Use Android's media controls",
            "On: it shows in the media controls, headset buttons work, and it pauses when other audio plays. Off: other audio, like a YouTube video, plays over it.",
            settings.systemControls, onChange = player::setSystemControls,
        )
        ToggleRow("Play/pause on the widget", "A button on the home-screen widget", settings.widgetButton, onChange = player::setWidgetButton)
        ToggleRow("Volume buttons on the widget", "− and + on the home-screen widget", settings.widgetVolume, onChange = player::setWidgetVolume)
    }
}
