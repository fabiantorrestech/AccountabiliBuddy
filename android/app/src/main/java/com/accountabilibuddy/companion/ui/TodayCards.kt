package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.wrapContentHeight
import androidx.compose.foundation.selection.toggleable
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.AlarmOn
import androidx.compose.material.icons.outlined.Smartphone
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.companion.sync.OpenComebacks
import com.accountabilibuddy.companion.sync.RunningTasks
import com.accountabilibuddy.companion.sync.deviceIdOf
import com.accountabilibuddy.companion.timers.TimerState
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbCheck
import com.accountabilibuddy.companion.ui.kit.AbInfoBanner
import com.accountabilibuddy.companion.ui.kit.AbRing
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTimerText
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.companion.usage.PhoneUsage
import com.accountabilibuddy.shared.sync.SessionTaskItem
import com.accountabilibuddy.shared.sync.SyncAction
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId
import kotlinx.coroutines.delay

/** "1h 10m", "2h", "40m": minutes as the ring card says them. */
internal fun minutesText(minutes: Int): String {
    val hours = minutes / 60
    val rest = minutes % 60
    return when {
        hours == 0 -> "${rest}m"
        rest == 0 -> "${hours}h"
        else -> "${hours}h ${rest}m"
    }
}

/**
 * Today's focus against the daily goal the PC sent (ADR 29): the accent ring, "1h 10m of 2h" and what is left. Without a goal it
 * is the number alone, and with nothing focused yet and no goal there is no card.
 */
@Composable
fun RingCard(focusedMinutes: Int, goalMinutes: Int?) {
    if (goalMinutes == null && focusedMinutes == 0) return
    AbCard(contentPadding = PaddingValues(horizontal = 20.dp, vertical = 16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            if (goalMinutes != null) AbRing(focusedMinutes.toFloat() / goalMinutes, diameter = 64.dp, strokeWidth = 9.dp)
            Column(Modifier.weight(1f)) {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.Bottom) {
                    AbTimerText(minutesText(focusedMinutes), style = Ab.type.timerSmall.copy(fontSize = 28.sp))
                    if (goalMinutes != null) Text("of ${minutesText(goalMinutes)}", style = Ab.type.body, color = Ab.colors.muted, modifier = Modifier.padding(bottom = 3.dp))
                }
                val caption = when {
                    goalMinutes == null -> "focused today"
                    focusedMinutes >= goalMinutes -> "Goal met"
                    else -> "focused today · ${minutesText(goalMinutes - focusedMinutes)} to go"
                }
                Text(caption, style = Ab.type.small, color = Ab.colors.muted)
            }
        }
    }
}

/**
 * "Come back to": the top two parked thoughts, tickable (ADR 30, 48); the whole list is in Tasks. Ticking a thought is what it was on
 * Today before: the shared list takes it at once, an older PC gets it as a sync action.
 */
@Composable
fun ComebackCard(comebacks: OpenComebacks?, onOpenTasks: () -> Unit) {
    val items = comebacks?.items.orEmpty()
    if (comebacks == null || items.isEmpty()) return
    val app = LocalContext.current.applicationContext as CompanionApp
    val received by app.sync.received.collectAsState()
    val pending by app.actions.pending.collectAsState()
    val legacySource = comebacks.legacySource
    val target = if (legacySource == null) null else deviceIdOf(legacySource, received)
    AbCard(contentPadding = PaddingValues(horizontal = 20.dp, vertical = 14.dp), verticalSpacing = 2.dp) {
        AbSectionLabel("Come back to")
        items.take(2).forEach { item ->
            val ticked = target != null && pending.any { it.target == target && it.item == item.id && it.kind == SyncAction.TICK_COMEBACK }
            val canTick = !ticked && (legacySource == null || target != null)
            TickRow(item.text, done = ticked, enabled = canTick) {
                if (legacySource == null) app.sharedLists.tickParked(item.id) else target?.let { app.actions.add(SyncAction.TICK_COMEBACK, it, item = item.id) }
            }
        }
        if (items.size > 2) {
            Text(
                "${items.size - 2} more in Tasks", style = Ab.type.small, color = Ab.colors.muted,
                modifier = Modifier.fillMaxWidth().heightIn(min = 44.dp).clickable(role = Role.Button, onClick = onOpenTasks).wrapContentHeight(Alignment.CenterVertically),
            )
        }
    }
}

/** One line with a tick box: a come-back, a step or a task. */
@Composable
internal fun TickRow(text: String, done: Boolean, enabled: Boolean, onTick: () -> Unit) {
    Row(
        Modifier.fillMaxWidth().heightIn(min = 48.dp)
            .toggleable(value = done, enabled = enabled, role = Role.Checkbox, onValueChange = { onTick() }),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        AbCheck(done)
        Text(
            text, style = Ab.type.body, modifier = Modifier.weight(1f),
            color = if (done) Ab.colors.muted else Ab.colors.ink,
            textDecoration = if (done) TextDecoration.LineThrough else null,
        )
    }
}

/**
 * The session's steps (board 5b): the steps of the task the PC shows, tickable; a task with no steps shows the session's tasks
 * instead. Ticks go to the PC as `tickTask` sync actions as before (ADR 24, 26); without a device group the list is read only.
 */
@Composable
fun StepsCard(running: RunningTasks) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val received by app.sync.received.collectAsState()
    val pending by app.actions.pending.collectAsState()
    val target = deviceIdOf(running.source, received)
    val shown = SessionTaskItem.shown(running.tasks, running.currentId)?.first
    val steps = shown?.subtasks.orEmpty()
    val rows = if (steps.isNotEmpty()) steps else running.tasks
    if (rows.isEmpty()) return
    AbCard(contentPadding = PaddingValues(horizontal = 20.dp, vertical = 14.dp), verticalSpacing = 2.dp) {
        AbSectionLabel(if (steps.isNotEmpty()) "Steps" else "This session")
        rows.forEach { row ->
            val sending = target != null && row.id != null && pending.any { it.target == target && it.item == row.id && it.kind == SyncAction.TICK_TASK }
            val done = row.isDone || sending
            TickRow(row.title, done = done, enabled = !done && target != null && row.id != null) {
                app.actions.add(SyncAction.TICK_TASK, target!!, item = row.id)
            }
        }
    }
}

/** "Phone guard is on. Settings rest until 2:20 PM." (board 5b); the first sentence goes when the phone's guard is turned off. */
@Composable
fun GuardBanner(lock: GuardsLock, guardOn: Boolean) {
    val time = lock.untilText()
    AbInfoBanner(
        buildAnnotatedString {
            if (guardOn) append("Phone guard is on. ")
            append("Settings rest until ")
            if (time != null) withStyle(SpanStyle(fontWeight = FontWeight.Bold)) { append(time) } else append("your session ends")
            append(".")
        },
    )
}

/** True while a timer runs or an alarm is set, so the nearest-timer card has something to show. */
@Composable
fun rememberHasTimerOrAlarm(): Boolean {
    val app = LocalContext.current.applicationContext as CompanionApp
    val timers by app.timers.timers.collectAsStateWithLifecycle()
    val alarms by app.timers.alarms.collectAsStateWithLifecycle()
    return timers.any { it.state == TimerState.RUNNING } || alarms.any { it.on && it.nextMs != null }
}

/** Two small cards side by side: the nearest timer or alarm, and today's time on the phone. One alone takes the width. */
@Composable
fun SmallCardsRow(hasTimer: Boolean, phoneUsage: PhoneUsage?, clock: ClockFormat, onAllowUsage: () -> Unit) {
    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        if (hasTimer) NearestTimerCard(clock, Modifier.weight(1f))
        // Without Usage access this card is the way to allow it (it was on the old Today).
        PhoneUseCard(phoneUsage, onAllowUsage, Modifier.weight(1f))
    }
}

@Composable
private fun NearestTimerCard(clock: ClockFormat, modifier: Modifier) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val timers by app.timers.timers.collectAsStateWithLifecycle()
    val alarms by app.timers.alarms.collectAsStateWithLifecycle()
    // Minutes only: looked at again every 15 seconds while the app is in front, in this card alone.
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(lifecycle) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                now = System.currentTimeMillis()
                delay(15_000)
            }
        }
    }
    val running = timers.filter { it.state == TimerState.RUNNING }.minByOrNull { it.leftMs(now) }
    if (running != null) {
        SmallCard(Icons.Outlined.Timer, running.name, "${leftText(running.leftMs(now))} left", modifier)
        return
    }
    val alarm = alarms.filter { it.on && it.nextMs != null }.minByOrNull { it.nextMs!! } ?: return
    val at = LocalDateTime.ofInstant(Instant.ofEpochMilli(alarm.nextMs!!), ZoneId.systemDefault())
    SmallCard(Icons.Outlined.AlarmOn, alarm.name, "Alarm · ${clock.time(at)}", modifier)
}

// 18 min, under 1 min, 1 h 10 min.
private fun leftText(ms: Long): String {
    val minutes = (ms + 59_999) / 60_000
    return when {
        minutes < 1 -> "under 1 min"
        minutes < 60 -> "$minutes min"
        else -> if (minutes % 60 == 0L) "${minutes / 60} h" else "${minutes / 60} h ${minutes % 60} min"
    }
}

@Composable
private fun PhoneUseCard(usage: PhoneUsage?, onAllowUsage: () -> Unit, modifier: Modifier) {
    if (usage == null) {
        SmallCard(Icons.Outlined.Smartphone, "Phone use", "Allow usage access", modifier, onClick = onAllowUsage)
    } else {
        SmallCard(Icons.Outlined.Smartphone, "${DayBarBuilder.duration(usage.total)} today", "on the phone", modifier)
    }
}

@Composable
private fun SmallCard(icon: ImageVector, title: String, subtitle: String, modifier: Modifier, onClick: (() -> Unit)? = null) {
    AbCard(modifier, onClick = onClick, contentPadding = PaddingValues(horizontal = 16.dp, vertical = 14.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Icon(icon, contentDescription = null, tint = Ab.colors.muted, modifier = Modifier.size(24.dp))
            Column(Modifier.weight(1f)) {
                Text(title, style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(subtitle, style = Ab.type.small, color = Ab.colors.muted, maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

/**
 * Step away (or I'm back) and End early… for a running session. Neither is the accent button. End early is the PC's own path (its
 * wait, then a reason); there is no "All tasks done" path on the phone, so the second button always reads End early… and never
 * ends a session by itself.
 */
@Composable
fun SessionActions(away: StepAwayState?, canEndEarly: Boolean, onEndEarly: () -> Unit, modifier: Modifier = Modifier) {
    Row(modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        AbTonalButton(if (away?.away == true) "I'm back" else "Step away", onClick = { away?.toggle?.invoke() }, enabled = away != null && !away.sending, modifier = Modifier.weight(1f))
        AbTonalButton("End early…", onClick = onEndEarly, enabled = canEndEarly, modifier = Modifier.weight(1f))
    }
}
