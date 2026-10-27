package com.accountabilibuddy.watch.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.focusable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.State
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.rotary.onRotaryScrollEvent
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.wear.compose.foundation.lazy.TransformingLazyColumn
import androidx.wear.compose.foundation.lazy.rememberTransformingLazyColumnState
import androidx.wear.compose.material3.Button
import androidx.wear.compose.material3.FilledIconButton
import androidx.wear.compose.material3.IconButton
import androidx.wear.compose.material3.OutlinedButton
import androidx.wear.compose.material3.Text
import com.accountabilibuddy.shared.WatchText
import com.accountabilibuddy.watch.timers.TimerStore
import com.accountabilibuddy.watch.timers.TimerText
import com.accountabilibuddy.watch.timers.WatchTimer
import java.time.Duration
import java.time.Instant

/**
 * The watch's own timers (V1, never sent anywhere): start 5, 10, 15 or 25 minutes, or a custom length set with the
 * crown; each running timer as a ring that empties as it goes with its time left, pause/resume, +1 min and dismiss.
 * One that ends buzzes like a phase end (the Alerts page's choices) and shows "Time's up" here.
 */
@Composable
fun TimersPage(now: State<Instant>, isCurrent: Boolean) {
    val context = LocalContext.current
    val timers by remember { TimerStore.timers(context) }.collectAsState()
    var choosing by remember { mutableStateOf(false) }
    // The page is composed ahead while it is next to the current one; the custom length is only kept while it is shown.
    LaunchedEffect(isCurrent) { if (!isCurrent) choosing = false }

    if (choosing) {
        CustomLength(
            isCurrent = isCurrent,
            onStart = { minutes ->
                TimerStore.start(context, Duration.ofMinutes(minutes.toLong()))
                choosing = false
            },
            onCancel = { choosing = false },
        )
        return
    }

    // The soonest end first; paused ones after the running, in the order they were made.
    val shown = timers.sortedWith(compareBy<WatchTimer> { it.isPaused }.thenBy { it.endsAt ?: Instant.MAX }.thenBy { it.id })
    TransformingLazyColumn(
        state = rememberTransformingLazyColumnState(),
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(6.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        item { Text("Timers", fontSize = 14.sp, fontWeight = FontWeight.Medium, color = Color(TIMER_COLOR), modifier = Modifier.padding(top = 24.dp)) }
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                PRESETS.forEach { minutes ->
                    FilledIconButton(onClick = { TimerStore.start(context, Duration.ofMinutes(minutes.toLong())) }, modifier = Modifier.size(40.dp)) {
                        Text("$minutes", fontSize = 14.sp)
                    }
                }
            }
        }
        item { OutlinedButton(onClick = { choosing = true }) { Text("Custom", fontSize = 13.sp) } }
        if (shown.isEmpty()) {
            item { Text("No timer running", fontSize = 12.sp, color = Color(0x99FFFFFF)) }
        }
        shown.forEach { timer ->
            item(key = timer.id) {
                TimerRow(
                    timer, now,
                    onPauseResume = { if (timer.isPaused) TimerStore.resume(context, timer.id) else TimerStore.pause(context, timer.id) },
                    onPlusMinute = { TimerStore.plusMinute(context, timer.id) },
                    onDismiss = { TimerStore.dismiss(context, timer.id) },
                )
            }
        }
        item { Box(Modifier.padding(bottom = 24.dp)) }
    }
}

// One timer: a small ring with the time left in it, its name and length, and its buttons. The clock is read only for a
// timer that is counting: a paused one doesn't change, and this row recomposes when a counting one ends, not every second.
@Composable
private fun TimerRow(timer: WatchTimer, now: State<Instant>, onPauseResume: () -> Unit, onPlusMinute: () -> Unit, onDismiss: () -> Unit) {
    val done by remember(timer) { derivedStateOf { !timer.isPaused && timer.isDone(now.value) } }
    val color = when {
        done -> Color(DONE_COLOR)
        timer.isPaused -> Color(0x99FFFFFF)
        else -> Color(TIMER_COLOR)
    }
    Row(
        Modifier.fillMaxWidth().padding(horizontal = 16.dp),
        horizontalArrangement = Arrangement.spacedBy(6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(52.dp), contentAlignment = Alignment.Center) {
            Canvas(Modifier.fillMaxSize()) {
                val ring = 4.dp.toPx()
                val radius = size.minDimension / 2 - ring / 2
                ringTrack(radius, ring, Color(0x26FFFFFF))
                // Read while drawing, so the ring is redrawn each second without recomposing the row.
                progressArc(timer.fractionLeft(timer.clockAt(now)), radius, ring, color)
            }
            TimerLeft(timer, now, done)
        }
        Column(Modifier.weight(1f)) {
            Text(
                if (done) "Time's up" else timer.name, fontSize = 12.sp, color = color, maxLines = 1, overflow = TextOverflow.Ellipsis,
            )
            Text(if (timer.isPaused) "Paused · ${TimerText.length(timer.length)}" else TimerText.length(timer.length), fontSize = 10.sp, color = Color(0x99FFFFFF))
            Row(horizontalArrangement = Arrangement.spacedBy(2.dp)) {
                if (!done) {
                    IconButton(onClick = onPauseResume, modifier = Modifier.size(32.dp)) { Text(if (timer.isPaused) "▶" else "⏸", fontSize = 13.sp) }
                }
                IconButton(onClick = onPlusMinute, modifier = Modifier.size(32.dp)) { Text("+1", fontSize = 12.sp) }
                IconButton(onClick = onDismiss, modifier = Modifier.size(32.dp)) { Text("✕", fontSize = 13.sp) }
            }
        }
    }
}

// The time left in a timer's ring: the only text that changes every second, so it is the only part that recomposes then.
@Composable
private fun TimerLeft(timer: WatchTimer, now: State<Instant>, done: Boolean) {
    Text(if (done) "0:00" else WatchText.timer(timer.left(timer.clockAt(now))), fontSize = 11.sp, color = Color.White, fontWeight = FontWeight.SemiBold)
}

// What a timer's time left is worked out from. A paused timer's doesn't depend on the clock, so it doesn't read it.
private fun WatchTimer.clockAt(now: State<Instant>): Instant = if (isPaused) Instant.EPOCH else now.value

// A custom length: the crown (a minute a step) or − / +, then Start.
@Composable
private fun CustomLength(isCurrent: Boolean, onStart: (Int) -> Unit, onCancel: () -> Unit) {
    var minutes by remember { mutableIntStateOf(DEFAULT_CUSTOM_MINUTES) }
    var turned by remember { mutableFloatStateOf(0f) }
    val focus = remember { FocusRequester() }
    LaunchedEffect(isCurrent) { if (isCurrent) runCatching { focus.requestFocus() } }

    Column(
        Modifier.fillMaxSize()
            .onRotaryScrollEvent { event ->
                if (!isCurrent) {
                    return@onRotaryScrollEvent false
                }

                turned += event.verticalScrollPixels
                val steps = (turned / STEP_PIXELS).toInt()
                if (steps != 0) {
                    turned -= steps * STEP_PIXELS
                    minutes = (minutes + steps).coerceIn(1, MAX_MINUTES)
                }
                true
            }
            .focusRequester(focus)
            .focusable(),
        verticalArrangement = Arrangement.spacedBy(6.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("Custom timer", fontSize = 13.sp, color = Color(TIMER_COLOR))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = { minutes = (minutes - 1).coerceAtLeast(1) }, modifier = Modifier.size(40.dp)) { Text("−", fontSize = 18.sp) }
            Text(WatchText.duration(minutes), fontSize = 26.sp, fontWeight = FontWeight.SemiBold, color = Color.White)
            IconButton(onClick = { minutes = (minutes + 1).coerceAtMost(MAX_MINUTES) }, modifier = Modifier.size(40.dp)) { Text("+", fontSize = 18.sp) }
        }
        Text("Crown: minutes", fontSize = 10.sp, color = Color(0x99FFFFFF))
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = onCancel, modifier = Modifier.size(40.dp)) { Text("✕", fontSize = 14.sp) }
            Button(onClick = { onStart(minutes) }) { Text("Start", fontSize = 13.sp) }
        }
    }
}

private val PRESETS = listOf(5, 10, 15, 25)
private const val DEFAULT_CUSTOM_MINUTES = 20
private const val MAX_MINUTES = 12 * 60
private const val STEP_PIXELS = 40f
/** Timers' teal, as the noise volume arc. */
private const val TIMER_COLOR = 0xFF2DD4BF
/** A timer that's up: the drift red. */
private const val DONE_COLOR = 0xFFF87171
