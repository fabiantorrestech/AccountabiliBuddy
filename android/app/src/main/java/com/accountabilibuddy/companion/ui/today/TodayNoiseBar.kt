package com.accountabilibuddy.companion.ui.today

import androidx.compose.ui.unit.sp
import androidx.compose.foundation.border
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.VolumeUp
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.SkipNext
import androidx.compose.material.icons.filled.SkipPrevious
import androidx.compose.material.icons.outlined.MusicNote
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Popup
import androidx.compose.ui.window.PopupProperties
import com.accountabilibuddy.companion.noise.NoisePlayer
import com.accountabilibuddy.companion.noise.NoiseSettings
import com.accountabilibuddy.companion.noise.NoiseState
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The noise bar in Today's thumb zone (brief §4.1, CN §3, board B-Phone-Session): the sound and whether it plays (tap to open the
 * Noise picker), previous, next, volume (a vertical 0–100 slider that pops up above the button) and play/pause as a filled 48 dp
 * button on the far right. Pausing doesn't hide it, so nothing moves (CN §1 rule 3).
 */
@Composable
fun TodayNoiseBar(state: NoiseState, settings: NoiseSettings, player: NoisePlayer, onOpenPicker: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    var volumeOpen by remember { mutableStateOf(false) }
    Row(
        modifier.fillMaxWidth().heightIn(min = 64.dp).padding(start = 8.dp, end = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Row(
            Modifier.weight(1f).heightIn(min = 48.dp).clickable(role = Role.Button, onClickLabel = "Choose a sound", onClick = onOpenPicker).padding(horizontal = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            // Accent while it plays, muted while paused (boards B-Phone-Session and Main).
            Icon(Icons.Outlined.MusicNote, contentDescription = null, tint = if (state.playing) colors.accent else colors.muted, modifier = Modifier.size(20.dp))
            Column(Modifier.weight(1f)) {
                Text(
                    state.title ?: if (state.fileCount == 0) "No sounds in that folder" else "Noise",
                    style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis,
                )
                Text(
                    when {
                        state.needsHeadphones -> "Connect headphones"
                        state.playing -> "Playing"
                        else -> "Paused"
                    },
                    style = Ab.type.small, color = if (state.needsHeadphones) colors.drift else colors.muted, maxLines = 1,
                )
            }
        }
        IconButton(onClick = player::previous) { Icon(Icons.Filled.SkipPrevious, contentDescription = "Previous sound", tint = colors.ink) }
        IconButton(onClick = player::next) { Icon(Icons.Filled.SkipNext, contentDescription = "Next sound", tint = colors.ink) }
        Box(contentAlignment = Alignment.Center) {
            // A filled circle behind the button while its slider is open.
            if (volumeOpen) Box(Modifier.size(44.dp).background(colors.surface2, CircleShape))
            IconButton(onClick = { volumeOpen = !volumeOpen }) {
                Icon(Icons.AutoMirrored.Outlined.VolumeUp, contentDescription = "Volume, ${settings.volume} percent", tint = colors.ink)
            }
            if (volumeOpen) VolumePopup(settings.volume, player, onDismiss = { volumeOpen = false })
        }
        Surface(
            onClick = player::toggle,
            modifier = Modifier.size(48.dp),
            shape = CircleShape,
            color = colors.accent,
            contentColor = colors.onAccent,
        ) {
            Box(contentAlignment = Alignment.Center) {
                Icon(
                    if (state.playing) Icons.Filled.Pause else Icons.Filled.PlayArrow,
                    contentDescription = if (state.playing) "Pause noise" else "Play noise",
                    modifier = Modifier.size(26.dp),
                )
            }
        }
    }
}

// The vertical slider, in a pill above the volume button (anchored to it, CN §1 rule 4), with the % on top. A tap outside closes it.
@Composable
private fun VolumePopup(volume: Int, player: NoisePlayer, onDismiss: () -> Unit) {
    var level by remember(volume) { mutableIntStateOf(volume) }
    val lift = with(LocalDensity.current) { 56.dp.roundToPx() }
    Popup(
        alignment = Alignment.BottomCenter,
        offset = IntOffset(0, -lift),
        onDismissRequest = onDismiss,
        properties = PopupProperties(focusable = true),
    ) {
        // Board B-Phone-Session: a 56 x 190 pill with a hairline, the % on top, an 8 dp track and a 24 dp thumb.
        Column(
            Modifier.size(width = 56.dp, height = 190.dp).background(Ab.colors.surface2, CircleShape).border(1.dp, Ab.colors.line, CircleShape)
                .padding(top = 14.dp, bottom = 12.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text("$level%", style = Ab.type.micro.copy(fontSize = 12.5.sp, letterSpacing = 0.sp, fontFeatureSettings = "tnum"), fontWeight = FontWeight.Bold, color = Ab.colors.ink)
            VerticalVolumeSlider(level, onChange = { level = it }, onDone = { player.setVolume(level) }, modifier = Modifier.padding(top = 6.dp), trackHeight = 138.dp)
        }
    }
}
