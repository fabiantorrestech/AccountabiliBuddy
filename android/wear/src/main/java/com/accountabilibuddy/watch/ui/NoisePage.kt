package com.accountabilibuddy.watch.ui

import androidx.compose.foundation.Canvas
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
 * The phone's noise player (ADR 36): the sound's name, previous, play/pause, next, and the volume as an arc around the
 * edge. On this page the crown changes the phone's noise volume, 5% a step.
 */
@Composable
fun NoisePage(isCurrent: Boolean) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val state by NoiseLink.state.collectAsState()
    val focus = remember { FocusRequester() }
    // Crown turns add up; every STEP_PIXELS is one 5% step.
    var turned by remember { mutableFloatStateOf(0f) }
    LaunchedEffect(isCurrent) { if (isCurrent) runCatching { focus.requestFocus() } }

    fun send(command: NoiseRemote.Command) = scope.launch { NoiseLink.send(context, command) }

    Box(
        Modifier.fillMaxSize()
            .onRotaryScrollEvent { event ->
                val current = state ?: return@onRotaryScrollEvent false
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
        val volume = state?.volume ?: 0
        Canvas(Modifier.fillMaxSize().padding(4.dp)) {
            val stroke = Stroke(width = 6.dp.toPx(), cap = StrokeCap.Round)
            drawArc(Color(0x33FFFFFF), startAngle = 135f, sweepAngle = 270f, useCenter = false, style = stroke)
            drawArc(Color(0xFF2DD4BF), startAngle = 135f, sweepAngle = 270f * volume / 100f, useCenter = false, style = stroke)
        }
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
            val current = state
            when {
                current == null -> Text("Open the phone app once to control its noise player", fontSize = 13.sp, textAlign = TextAlign.Center, modifier = Modifier.padding(horizontal = 24.dp))
                !current.hasFolder -> Text("Choose a folder of sounds on the phone", fontSize = 13.sp, textAlign = TextAlign.Center, modifier = Modifier.padding(horizontal = 24.dp))
                else -> {
                    Text("${current.volume}%", fontSize = 12.sp, color = Color(0xFF99F6E4))
                    Text(
                        current.title ?: "Noise", fontSize = 14.sp, maxLines = 2, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center,
                        modifier = Modifier.padding(horizontal = 28.dp),
                    )
                    Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
                        IconButton(onClick = { send(NoiseRemote.Command(NoiseRemote.PREVIOUS)) }) { Text("⏮", fontSize = 18.sp) }
                        FilledIconButton(onClick = { send(NoiseRemote.Command(NoiseRemote.TOGGLE)) }, modifier = Modifier.size(56.dp)) {
                            Text(if (current.playing) "⏸" else "▶", fontSize = 22.sp)
                        }
                        IconButton(onClick = { send(NoiseRemote.Command(NoiseRemote.NEXT)) }) { Text("⏭", fontSize = 18.sp) }
                    }
                    Text("Crown: volume", fontSize = 10.sp, color = Color(0x99FFFFFF))
                }
            }
        }
    }
}

private const val STEP = 5
private const val STEP_PIXELS = 40f
