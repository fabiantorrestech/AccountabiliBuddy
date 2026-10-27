package com.accountabilibuddy.watch.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.clickable
import androidx.compose.foundation.focusable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.rotary.onRotaryScrollEvent
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.wear.compose.material3.FilledIconButton
import androidx.wear.compose.material3.IconButton
import androidx.wear.compose.material3.Text
import com.accountabilibuddy.shared.NoiseRemote
import com.accountabilibuddy.watch.link.NoiseLink
import kotlinx.coroutines.launch

/**
 * The noise player (ADR 36): the sound's name, previous, play/pause, next, and the volume as an arc around the edge.
 * On this page the crown changes the volume, 5% a step. A small lock shows while the player is locked to headphones and
 * none are connected (play does nothing then). It is the phone's player, or (ADR 42), when the phone lists PCs with a
 * noise player, one of theirs: tap the device name at the top to go This phone, then each PC, and round again (the
 * pick is remembered). A PC is reached through the phone and the sync files, so what it shows is a few seconds old and a
 * command takes a few seconds; the page says so, and says it is sending while a command waits.
 */
@Composable
fun NoisePage(isCurrent: Boolean) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val state by NoiseLink.state.collectAsState()
    val chosen by NoiseLink.chosen.collectAsState()
    val overrides by NoiseLink.overrides.collectAsState()
    val focus = remember { FocusRequester() }
    // Crown turns add up; every STEP_PIXELS is one 5% step.
    var turned by remember { mutableFloatStateOf(0f) }
    LaunchedEffect(isCurrent) { if (isCurrent) runCatching { focus.requestFocus() } }
    LaunchedEffect(Unit) { NoiseLink.load(context) }

    val pcs = state?.devices.orEmpty()
    // A picked PC the phone no longer lists shows the phone's player (the pick stays for when it is back).
    val pc = pcs.firstOrNull { it.id == chosen }?.let { NoiseLink.shown(it, overrides[it.id]) }
    val target = state?.let { phone -> pc?.let(::pcTarget) ?: phoneTarget(phone) }

    fun send(command: NoiseRemote.Command) = scope.launch { NoiseLink.send(context, command.copy(device = target?.deviceId)) }

    // This phone, then each PC in the phone's order, then round again.
    fun pickNext() {
        val ids = listOf<String?>(null) + pcs.map { it.id }
        val at = ids.indexOf(pc?.id).coerceAtLeast(0)
        NoiseLink.choose(context, ids[(at + 1) % ids.size])
    }

    Box(
        Modifier.fillMaxSize()
            .onRotaryScrollEvent { event ->
                val current = target ?: return@onRotaryScrollEvent false
                turned += event.verticalScrollPixels
                val steps = (turned / STEP_PIXELS).toInt()
                if (steps != 0) {
                    turned -= steps * STEP_PIXELS
                    send(NoiseRemote.Command(NoiseRemote.VOLUME, (current.volume + steps * STEP).coerceIn(0, 100)))
                }
                true
            }
            .focusRequester(focus)
            .focusable(),
        contentAlignment = Alignment.Center,
    ) {
        val volume = target?.volume ?: 0
        Canvas(Modifier.fillMaxSize().padding(4.dp)) {
            val stroke = Stroke(width = 6.dp.toPx(), cap = StrokeCap.Round)
            drawArc(Color(0x33FFFFFF), startAngle = 135f, sweepAngle = 270f, useCenter = false, style = stroke)
            drawArc(Color(0xFF2DD4BF), startAngle = 135f, sweepAngle = 270f * volume / 100f, useCenter = false, style = stroke)
        }
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(if (pcs.isEmpty()) 6.dp else 3.dp)) {
            if (pcs.isNotEmpty() && target != null) {
                // Tap to switch device.
                Text(
                    "⇄ ${target.deviceName}", fontSize = 11.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, color = Color(0xFF99F6E4),
                    modifier = Modifier.clickable { pickNext() }.padding(horizontal = 28.dp, vertical = 2.dp),
                )
            }
            when {
                target == null -> Text("Open the phone app once to control its noise player", fontSize = 13.sp, textAlign = TextAlign.Center, modifier = Modifier.padding(horizontal = 24.dp))
                !target.hasFolder -> Text("Choose a folder of sounds on the phone", fontSize = 13.sp, textAlign = TextAlign.Center, modifier = Modifier.padding(horizontal = 24.dp))
                else -> {
                    Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                        if (target.locked) LockMark()
                        Text(if (target.locked) "Connect headphones" else "${target.volume}%", fontSize = 12.sp,
                            color = if (target.locked) Color(0xFFFCA5A5) else Color(0xFF99F6E4))
                    }
                    Text(
                        target.title ?: "Noise", fontSize = 14.sp, maxLines = if (pcs.isEmpty()) 2 else 1, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center,
                        modifier = Modifier.padding(horizontal = 28.dp),
                    )
                    Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
                        IconButton(onClick = { send(NoiseRemote.Command(NoiseRemote.PREVIOUS)) }) { Text("⏮", fontSize = 18.sp) }
                        FilledIconButton(onClick = { send(NoiseRemote.Command(NoiseRemote.TOGGLE)) }, modifier = Modifier.size(56.dp)) {
                            Text(if (target.playing) "⏸" else "▶", fontSize = 22.sp)
                        }
                        IconButton(onClick = { send(NoiseRemote.Command(NoiseRemote.NEXT)) }) { Text("⏭", fontSize = 18.sp) }
                    }
                    Text(
                        target.status ?: "Crown: volume", fontSize = 10.sp, maxLines = 2, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center,
                        color = if (target.warning) Color(0xFFFCA5A5) else Color(0x99FFFFFF),
                        modifier = Modifier.padding(horizontal = 28.dp),
                    )
                }
            }
        }
    }
}

/**
 * What the page shows for one player. [deviceId] is the PC's sync device id, null for this phone. [status] replaces the
 * crown hint: how a PC's answer is on its way, or its refusal ([warning]).
 */
private data class Target(
    val deviceId: String?,
    val deviceName: String,
    val playing: Boolean,
    val title: String?,
    val volume: Int,
    val locked: Boolean,
    val hasFolder: Boolean,
    val status: String?,
    val warning: Boolean,
)

private fun phoneTarget(state: NoiseRemote.State) =
    Target(null, "This phone", state.playing, state.title, state.volume, state.locked, state.hasFolder, status = null, warning = false)

private fun pcTarget(pc: NoiseRemote.PcPlayer) = Target(
    pc.id, pc.name, pc.playing, pc.title, pc.volume, pc.locked, hasFolder = true,
    status = when {
        pc.note != null -> pc.note
        pc.sending -> "Sending to ${pc.name}…\nAn idle PC can take minutes"
        else -> "Crown: volume\nUpdates take a few seconds"
    },
    warning = pc.note != null,
)

// A small padlock (the watch has no icon library): a shackle over a body.
@Composable
private fun LockMark() {
    Canvas(Modifier.size(10.dp, 12.dp)) {
        val color = Color(0xFFFCA5A5)
        val body = size.height * 0.55f
        drawRoundRect(color, topLeft = androidx.compose.ui.geometry.Offset(0f, size.height - body),
            size = androidx.compose.ui.geometry.Size(size.width, body), cornerRadius = androidx.compose.ui.geometry.CornerRadius(1.5.dp.toPx()))
        val inset = size.width * 0.2f
        drawArc(color, startAngle = 180f, sweepAngle = 180f, useCenter = false,
            topLeft = androidx.compose.ui.geometry.Offset(inset, 0.5.dp.toPx()),
            size = androidx.compose.ui.geometry.Size(size.width - 2 * inset, (size.height - body) * 1.6f),
            style = Stroke(width = 1.5.dp.toPx()))
    }
}

private const val STEP = 5
private const val STEP_PIXELS = 40f
