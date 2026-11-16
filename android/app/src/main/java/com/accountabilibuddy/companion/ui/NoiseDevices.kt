package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.Sync
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.NoiseDevice
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Duration
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.util.UUID

/** After this long without a newer file, a device's row is greyed with when it was last seen. */
private val STALE_AFTER: Duration = Duration.ofMinutes(15)
private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

/**
 * The volume popover's "Other devices" button (ADR 92): the rows of the other devices' noise players stay hidden behind it until
 * it is pressed ([open] is remembered on this phone). Filled behind it while the rows show.
 */
@Composable
fun NoiseDevicesButton(open: Boolean, onToggle: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    IconButton(
        onClick = onToggle,
        modifier = modifier.size(40.dp).clip(RoundedCornerShape(50)).background(if (open) colors.accentSoft else androidx.compose.ui.graphics.Color.Transparent),
    ) {
        Icon(Icons.Outlined.Devices, contentDescription = if (open) "Hide other devices" else "Other devices", tint = if (open) colors.accent else colors.muted, modifier = Modifier.size(20.dp))
    }
}

/**
 * One row per other device with a noise player (ADR 92). A playing device leads with ▶ and is tinted; a paused one leads with ⏸
 * and isn't. A tap asks it to pause (or play): the row turns at once, with a small sync mark until that device answers. A refusal
 * or "didn't answer" shows on the row's second line for a few seconds.
 */
@Composable
fun NoiseDeviceRows(devices: List<NoiseDevice>, onSetPlaying: (UUID, Boolean) -> Unit, modifier: Modifier = Modifier, now: Instant = Instant.now()) {
    val colors = Ab.colors
    Column(modifier, verticalArrangement = Arrangement.spacedBy(4.dp)) {
        if (devices.isEmpty()) {
            Text("No other device has a noise player.", style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(8.dp))
        }
        devices.forEach { device ->
            val stale = Duration.between(device.seenAt, now) > STALE_AFTER
            val second = device.note
                ?: if (stale) "Last seen ${CLOCK.format(device.seenAt.atZone(ZoneId.systemDefault()))}" else null
                ?: if (device.locked && !device.playing) "Headphones not connected" else null
                ?: device.title
                ?: if (device.playing) "Playing" else "Paused"
            val action = if (device.playing) "Pause noise on ${device.name}" else "Play noise on ${device.name}"
            Row(
                Modifier.fillMaxWidth().heightIn(min = 52.dp).clip(RoundedCornerShape(12.dp))
                    .background(if (device.playing) colors.accentSoft else androidx.compose.ui.graphics.Color.Transparent)
                    .clickable(role = Role.Button, onClickLabel = action) { onSetPlaying(device.id, !device.playing) }
                    .semantics { contentDescription = "${device.name}, ${if (device.playing) "playing" else "paused"}. $second" }
                    .alpha(if (stale) 0.55f else 1f)
                    .padding(horizontal = 10.dp, vertical = 6.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Icon(
                    if (device.playing) Icons.Filled.PlayArrow else Icons.Filled.Pause, contentDescription = null,
                    tint = if (device.playing) colors.accent else colors.muted, modifier = Modifier.size(20.dp),
                )
                Column(Modifier.weight(1f)) {
                    Text(device.name, style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(second, style = Ab.type.small, color = if (device.note != null) colors.drift else colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                if (device.sending) {
                    Icon(Icons.Outlined.Sync, contentDescription = "Waiting for ${device.name}", tint = colors.muted, modifier = Modifier.size(16.dp))
                }
            }
        }
    }
}
