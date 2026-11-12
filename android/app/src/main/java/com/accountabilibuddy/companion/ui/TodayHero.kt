package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.TodayInfo
import com.accountabilibuddy.companion.sync.PcState
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbTimerText
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.SyncAction
import kotlinx.coroutines.delay
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * A clock that moves on at each whole second while the app is in front (and stops when it is not). The one place Today ticks each
 * second: a composable reads `.value` in its own body, so only that composable is redrawn, never the cards around it.
 */
@Composable
fun rememberTickingNow(): State<Instant> {
    val now = remember { mutableStateOf(Instant.now()) }
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(lifecycle) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                now.value = Instant.now()
                delay(1000 - System.currentTimeMillis() % 1000)
            }
        }
    }
    return now
}

/**
 * Idle (board 5a): "Nothing running." in ink, what comes next in muted text, and the one way to start a session. [compact] (the
 * phone's round 2 Today, board Main) says both on one line; with [showStart] false the Start button is left to the thumb zone's
 * action bar ([canStartHere] says when it shows), and only the lines that explain why Start isn't offered stay here.
 */
@Composable
fun IdleHero(
    info: TodayInfo?,
    fallbackNext: String,
    pc: TodayPc,
    clock: ClockFormat,
    locked: Boolean,
    onStart: () -> Unit,
    onCancelWaiting: () -> Unit,
    compact: Boolean = false,
    showStart: Boolean = true,
) {
    // While the settings rest for a PC session this screen cannot see (its file is out of date), "Nothing running." would be untrue.
    val headline = if (locked) "Session running." else info?.headline ?: TodayInfo.NOTHING_RUNNING
    val next = if (locked) "Its PC has not synced for a while." else info?.next ?: fallbackNext
    if (compact) {
        Text(
            buildAnnotatedString {
                withStyle(SpanStyle(color = Ab.colors.ink, fontWeight = FontWeight.SemiBold)) { append(headline) }
                append(" ")
                withStyle(SpanStyle(color = Ab.colors.muted)) { append(next) }
            },
            style = Ab.type.bodyMedium,
        )
    } else {
        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(headline, style = Ab.type.title, color = Ab.colors.ink)
            Text(next, style = Ab.type.title, color = Ab.colors.muted)
        }
    }
    val startHere = canStartHere(pc, locked)
    if (startHere && !showStart) return
    Column(Modifier.padding(top = if (compact) 8.dp else 16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        when {
            !pc.hasPc -> MutedLine("Link a PC to start sessions from here.")
            pc.updateNeeded != null -> MutedLine(pc.updateNeeded)
            pc.waiting != null -> WaitingLine(pc, clock, onCancelWaiting)
            // The settings rest for a PC session whose file is out of date (not the phone's own test session: a real session comes
            // first, so Start stays).
            locked -> MutedLine("Start comes back when the session ends or the PC syncs.")
            else -> AbPrimaryButton("Start on ${pc.name}", onClick = onStart, modifier = Modifier.fillMaxWidth())
        }
    }
}

/** True when a session can be started on [pc] from this phone now (the Start button shows). */
fun canStartHere(pc: TodayPc, locked: Boolean): Boolean = pc.hasPc && pc.updateNeeded == null && pc.waiting == null && !locked

/**
 * A running session (boards 5b, 6a): the timer at 72 sp, the task and when it ends. Only [SessionTimer] redraws each second.
 * [awayText] says that the session is stepped away or that a step away is on its way.
 */
@Composable
fun SessionHero(state: PcState, taskTitle: String?, clock: ClockFormat, awayText: String?, awaySince: Instant?) {
    val zone = ZoneId.systemDefault()
    val end = state.phaseEndsAt?.toInstant()
    // While stepped away the PC pauses the countdown (it moves the phase's start and end only on return): the timer holds at the
    // moment of stepping away, and the end time is not a promise.
    val endText = when {
        state.pcOff -> "PC off · paused"
        awaySince != null -> "paused"
        end != null -> "ends ${clock.time(LocalDateTime.ofInstant(end, zone))}"
        else -> "no set end"
    }
    val what = taskTitle ?: state.profileName ?: "Focus"
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        SessionTimer(startedAt = state.phaseStartedAt?.toInstant(), endsAt = end, isBreak = state.isBreak, awaySince = awaySince)
        Text("$what · $endText", style = Ab.type.bodyMedium, color = Ab.colors.muted, maxLines = 2, overflow = TextOverflow.Ellipsis)
        if (awayText != null) Text(awayText, style = Ab.type.small, color = Ab.colors.muted)
    }
}

/** The big timer: counts down to the phase's end, or up from its start when it has no set end; 0:00 once the end has passed. */
@Composable
private fun SessionTimer(startedAt: Instant?, endsAt: Instant?, isBreak: Boolean, awaySince: Instant?) {
    val ticking by rememberTickingNow()
    // Counts from the moment of stepping away while away (the PC's CountdownTime does the same).
    val now = awaySince?.takeIf { it.isBefore(ticking) } ?: ticking
    val seconds = when {
        endsAt != null -> secondsLeft(now, endsAt)
        startedAt != null -> Duration.between(startedAt, now).seconds.coerceAtLeast(0)
        else -> 0L
    }
    BigTimer(clockText(seconds), color = if (isBreak) Ab.meaning(Ab.colors.sleep) else Ab.colors.ink)
}

/**
 * The PC's planning stage (it is not focus time): a calm count of the minutes left to plan, with a thin bar. It keeps counting
 * past its time until "Ready?" is answered on the PC. [startedAt] and [endsAt] are the stage's own.
 */
@Composable
fun PlanningHero(startedAt: Instant?, endsAt: Instant, profileName: String?) {
    val now by rememberTickingNow()
    val left = secondsLeft(now, endsAt)
    val span = startedAt?.let { Duration.between(it, endsAt).seconds.coerceAtLeast(1) } ?: 1L
    val remaining = if (startedAt == null) 0f else (left.toFloat() / span).coerceIn(0f, 1f)
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        BigTimer(clockText(left), color = Ab.colors.muted)
        Box(Modifier.fillMaxWidth().height(6.dp).clip(CircleShape).background(Ab.colors.surface2)) {
            Box(Modifier.fillMaxHeight().fillMaxWidth(remaining).background(Ab.meaning(Ab.colors.sleep), CircleShape))
        }
        Text(
            if (left == 0L) "Time is up. Say Ready on the PC when you are." else "left to plan · ${profileName ?: "Focus"}",
            style = Ab.type.bodyMedium, color = Ab.colors.muted,
        )
        Text(
            "Write everything you need to do on paper first. Then pick at most 3 for this session; even 1 is great.",
            style = Ab.type.small, color = Ab.colors.muted,
        )
    }
}

// The timer is a display number: it keeps its size when the phone's font size is large (a 72 sp number at 1.3 would not fit), and
// an hour or more (h:mm:ss) steps down so seven characters fit a narrow screen.
@Composable
internal fun BigTimer(text: String, color: androidx.compose.ui.graphics.Color) {
    val density = LocalDensity.current
    val style = if (text.length > 5) Ab.type.timer.copy(fontSize = 52.sp, lineHeight = 57.sp) else Ab.type.timer
    CompositionLocalProvider(LocalDensity provides Density(density.density, fontScale = minOf(density.fontScale, 1f))) {
        AbTimerText(text, style = style, color = color)
    }
}

internal fun secondsLeft(now: Instant, end: Instant): Long = ((Duration.between(now, end).toMillis() + 999) / 1000).coerceAtLeast(0)

/** 18:42, or 1:18:42 from an hour. */
internal fun clockText(totalSeconds: Long): String {
    val hours = totalSeconds / 3600
    val minutes = (totalSeconds % 3600) / 60
    val seconds = totalSeconds % 60
    return if (hours > 0) "%d:%02d:%02d".format(hours, minutes, seconds) else "%d:%02d".format(minutes, seconds)
}

@Composable
internal fun MutedLine(text: String) {
    Text(text, style = Ab.type.bodyMedium, color = Ab.colors.muted)
}

/** A start, end-early or step-away request waiting for the PC's answer in its file, with "Stop waiting". */
@Composable
fun WaitingLine(pc: TodayPc, clock: ClockFormat, onCancel: () -> Unit) {
    val waiting = pc.waiting ?: return
    val what = when (waiting.kind) {
        SyncAction.START -> "Starting on ${pc.name}"
        SyncAction.END_EARLY_BEGIN -> "Asking ${pc.name} to end early"
        else -> "Ending the session on ${pc.name}"
    }
    val sent = clock.time(LocalDateTime.ofInstant(waiting.at, ZoneId.systemDefault()))
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        Icon(Icons.Outlined.Schedule, contentDescription = null, tint = Ab.meaning(Ab.colors.waiting), modifier = Modifier.size(20.dp))
        Text(
            "$what. Sent $sent; the PC answers when it next syncs.",
            style = Ab.type.bodyMedium, color = Ab.colors.muted, modifier = Modifier.weight(1f),
        )
    }
    AbTonalButton("Stop waiting", onClick = onCancel, modifier = Modifier.fillMaxWidth())
}
