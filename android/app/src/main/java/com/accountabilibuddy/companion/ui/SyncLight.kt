package com.accountabilibuddy.companion.ui

import android.widget.Toast
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Sync
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Instant
import kotlinx.coroutines.delay

/** The smallest touch target of the sync light's dot row and refresh button: 44 dp on a phone, 48 dp in the tablet's sidebar. */
val LocalSyncTouch = androidx.compose.runtime.compositionLocalOf { 44.dp }

/**
 * "Sync now" for the whole phone: reads the sync folder, Drive and the relay, redraws, and says what happened. One holder
 * so the light's refresh button and its popover share the "syncing" state.
 */
class SyncNow(private val context: android.content.Context) {
    var refreshing by mutableStateOf(false)
        private set

    fun run() {
        if (refreshing) return
        refreshing = true
        (context.applicationContext as CompanionApp).refreshNow { text ->
            refreshing = false
            // Long: the line can name who answered.
            Toast.makeText(context, text, Toast.LENGTH_LONG).show()
        }
    }
}

@Composable
fun rememberSyncNow(): SyncNow {
    val context = LocalContext.current
    return remember { SyncNow(context.applicationContext) }
}

/**
 * The sync light on Today's date line (board 20a): a 7 dp dot and a 13 dp glyph for each device, then the 44 dp refresh. Tap
 * the dot and glyphs for the popover. The light itself is hidden when this phone isn't linked to anything, or when the setting in
 * Devices & sync says so (Off, or Only when something's wrong while all is well); the refresh stays.
 */
@Composable
fun SyncLightHost(
    devices: DevicesState,
    watch: WatchSectionState,
    detectedWatchIds: Set<String>,
    syncNow: SyncNow,
    onOpenDevices: () -> Unit,
    modifier: Modifier = Modifier,
) {
    // The ages move on: worked out again every 30 s, only while the app is in front.
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(lifecycle) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                now = Instant.now()
                delay(30_000)
            }
        }
    }
    // Worked out again only when something it reads changes or the tick moves on, not on every recomposition of Today.
    val refreshing = syncNow.refreshing
    val model = remember(devices, watch, detectedWatchIds, refreshing, now) { SyncLightRules.of(devices, watch, detectedWatchIds, refreshing, now) }
    val setting by UiPrefs.syncLight.collectAsState()
    SyncLight(model, syncNow.refreshing, onSyncNow = syncNow::run, onOpenDevices = onOpenDevices, modifier = modifier, setting = setting)
}

@Composable
fun SyncLight(
    model: SyncLightModel?,
    refreshing: Boolean,
    onSyncNow: () -> Unit,
    onOpenDevices: () -> Unit,
    modifier: Modifier = Modifier,
    /** The Settings › Devices & sync choice: it only hides the dot and glyphs. The ↻ stays, so nothing about syncing changes. */
    setting: SyncLightSetting = SyncLightSetting.On,
) {
    Row(modifier, verticalAlignment = Alignment.CenterVertically) {
        if (model != null && setting.shows(model)) {
            var open by remember { mutableStateOf(false) }
            Box {
                Row(
                    Modifier.heightIn(min = LocalSyncTouch.current).clickable(role = Role.Button) { open = true }
                        .semantics { contentDescription = "Sync status: ${model.headline}" }.padding(horizontal = 6.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(7.dp),
                ) {
                    SyncDotView(model.dot)
                    model.devices.forEach { SyncGlyph(it.kind, if (it.dimmed) Ab.colors.faint else Ab.colors.muted) }
                }
                if (open) {
                    SyncLightPopover(model, refreshing, onSyncNow = onSyncNow, onOpenDevices = { open = false; onOpenDevices() }, onDismiss = { open = false })
                }
            }
            Box(Modifier.padding(horizontal = 2.dp).width(1.dp).height(20.dp).background(Ab.colors.line))
        }
        RefreshButton(refreshing, onSyncNow)
    }
}

/** The 7 dp dot: filled in its state's colour, or a hollow ring while syncing. */
@Composable
private fun SyncDotView(dot: SyncDot) {
    val colors = Ab.colors
    val colour = when (dot) {
        SyncDot.InSync, SyncDot.Syncing -> colors.met
        SyncDot.SeveralBehind -> colors.sleep
        SyncDot.Offline -> colors.drift
    }
    if (dot == SyncDot.Syncing) {
        Box(Modifier.size(7.dp).border(1.5.dp, colour, CircleShape))
    } else {
        Box(Modifier.size(7.dp).background(colour, CircleShape))
    }
}

/** The ↻ at the end of the light: 44 dp to touch, spins while the sync runs. */
@Composable
private fun RefreshButton(refreshing: Boolean, onClick: () -> Unit) {
    val spinning = refreshing && LocalMotionScale.current > 0f
    val angle = if (spinning) {
        val transition = rememberInfiniteTransition(label = "sync spin")
        val turn by transition.animateFloat(0f, 360f, infiniteRepeatable(tween(900, easing = LinearEasing), RepeatMode.Restart), label = "turn")
        turn
    } else {
        0f
    }
    AbQuietIconButton(Icons.Outlined.Sync, contentDescription = "Sync now", onClick = onClick, enabled = !refreshing, modifier = Modifier.size(LocalSyncTouch.current).rotate(angle))
}

/** A device's outline (PC, phone or watch), 13 dp, drawn from [DeviceKind.iconPath] in a 16 x 16 box. */
@Composable
fun SyncGlyph(kind: DeviceKind, tint: Color, size: androidx.compose.ui.unit.Dp = 13.dp) {
    val path = remember(kind) { PathParser().parsePathString(kind.iconPath).toPath() }
    Canvas(Modifier.size(size)) {
        scale(this.size.minDimension / 16f, pivot = Offset.Zero) {
            drawPath(path, tint, style = Stroke(width = 1.4f, cap = StrokeCap.Round, join = StrokeJoin.Round))
        }
    }
}
