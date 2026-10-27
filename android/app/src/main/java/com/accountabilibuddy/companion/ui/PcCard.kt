package com.accountabilibuddy.companion.ui

import android.os.SystemClock
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.selection.selectable
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.link.EndEarlyTerms
import com.accountabilibuddy.companion.link.LinkStatus
import com.accountabilibuddy.companion.link.PairingLink
import com.accountabilibuddy.companion.link.PcState
import com.accountabilibuddy.shared.sync.SyncAction
import kotlinx.coroutines.delay
import java.time.Duration
import java.time.OffsetDateTime
import java.time.ZoneId

private val LENGTHS = listOf(25, 50, 90)

// The phone's and the PC's clocks may differ by a little: through the sync files the phone sends a little after the wait.
private const val SKEW_SECONDS = 3L

/** Today: the linked PC's session, with Start (idle) or End early (running). Hidden while no PC is linked. */
@Composable
fun PcCard(
    status: LinkStatus,
    clock: ClockFormat,
    busy: Boolean,
    onStart: (profileId: String?, minutes: Int?, countUp: Boolean) -> Unit,
    onEndEarly: () -> Unit,
    onRetry: () -> Unit,
) {
    if (status is LinkStatus.Unlinked) {
        return
    }

    var choosingStart by remember { mutableStateOf(false) }
    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            when (status) {
                is LinkStatus.Connected -> {
                    IconTitle("PC · ${status.pcName}", style = MaterialTheme.typography.titleMedium)
                    val state = status.state
                    if (state == null) {
                        Text("Connected…", style = MaterialTheme.typography.bodyMedium)
                    } else {
                        SessionLine(state, clock)
                        if (state.isActive) {
                            OutlinedButton(onClick = onEndEarly, enabled = !busy) { Text("End early…") }
                        } else {
                            Button(onClick = { choosingStart = true }, enabled = !busy && state.profiles.isNotEmpty()) { Text("Start on the PC…") }
                        }
                    }

                    if (choosingStart && state != null) {
                        StartOnPcDialog(
                            state = state,
                            onStart = { profileId, minutes, countUp ->
                                choosingStart = false
                                onStart(profileId, minutes, countUp)
                            },
                            onCancel = { choosingStart = false },
                        )
                    }
                }

                is LinkStatus.Offline -> {
                    IconTitle("PC", style = MaterialTheme.typography.titleMedium)
                    // The first sentence ("Can't reach DESK-PC"); the technical reason behind ⓘ.
                    Text(status.message.substringBefore(" (").substringBefore("."), style = MaterialTheme.typography.bodyMedium)
                    OutlinedButton(onClick = onRetry) { Text("Try again") }
                    Hint(status.message, label = "Details")
                }

                else -> {
                    IconTitle("PC", style = MaterialTheme.typography.titleMedium)
                    Text("Connecting…", style = MaterialTheme.typography.bodyMedium)
                }
            }
        }
    }
}

/** The planning stage's own colour, the same as on the PC (a periwinkle blue). */
private val PLANNING_COLOR = androidx.compose.ui.graphics.Color(0xFF7C9EF2)

/**
 * The PC's session: the line [describe] gives, or, while the PC is planning (before its focus countdown; not focus time), "Planning · 3 min
 * left" with its own bar in the planning colour. Planning keeps counting past its time until the PC's "Ready?" card is answered there.
 */
@Composable
private fun SessionLine(state: PcState, clock: ClockFormat) {
    val planningEnd = state.planningEndsAt
    if (!state.isPlanning || planningEnd == null) {
        Text(describe(state, clock), style = MaterialTheme.typography.bodyLarge)
        return
    }

    var now by remember { mutableStateOf(OffsetDateTime.now()) }
    LaunchedEffect(planningEnd) {
        while (true) {
            delay(1000)
            now = OffsetDateTime.now()
        }
    }

    val start = state.planningStartedAt ?: now
    val left = Duration.between(now, planningEnd)
    val span = Duration.between(start, planningEnd).seconds.coerceAtLeast(1)
    val remaining = if (left.isNegative) 0f else (left.seconds.toFloat() / span).coerceIn(0f, 1f)
    val minutes = Math.ceil(left.seconds / 60.0).toInt()
    val what = if (left.isNegative || left.isZero) "time is up · waiting for Ready? on the PC" else "$minutes min left"
    Text("Planning · ${state.profileName ?: "a profile"} · $what", style = MaterialTheme.typography.bodyLarge, color = PLANNING_COLOR)
    LinearProgressIndicator(progress = { remaining }, color = PLANNING_COLOR, modifier = Modifier.fillMaxWidth())
}

/** E.g. "Focus · Coding until 12:00 PM (24 min left)", "Blocks on · Work until 5:00 PM", "Focus · Quick Start · 1h 12m so far". */
private fun describe(state: PcState, clock: ClockFormat): String {
    if (!state.isActive) {
        return "Nothing running on the PC."
    }

    val profile = state.profileName ?: "a profile"
    val end = state.phaseEndsAt
    val kind = when {
        state.isBreak -> "Break"
        state.isScheduledBlock -> "Blocks on · $profile"
        else -> "Focus · $profile"
    }
    if (end == null) {
        val since = state.phaseStartedAt ?: return kind
        return "$kind · ${DayBarBuilder.duration(Duration.between(since, OffsetDateTime.now()))} so far"
    }

    val local = end.atZoneSameInstant(ZoneId.systemDefault()).toLocalDateTime()
    val left = Duration.between(OffsetDateTime.now(), end)
    return "$kind until ${clock.time(local)} (${DayBarBuilder.duration(if (left.isNegative) Duration.ZERO else left)} left)"
}

@Composable
private fun StartOnPcDialog(state: PcState, onStart: (String?, Int?, Boolean) -> Unit, onCancel: () -> Unit) {
    var profileId by remember { mutableStateOf(state.profiles.firstOrNull()?.id) }
    var minutes by remember { mutableIntStateOf(LENGTHS.first()) }
    AlertDialog(
        onDismissRequest = onCancel,
        title = { Text("Start on the PC") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                state.profiles.forEach { profile ->
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier.fillMaxWidth().selectable(selected = profileId == profile.id, onClick = { profileId = profile.id }),
                    ) {
                        RadioButton(selected = profileId == profile.id, onClick = { profileId = profile.id })
                        Text(profile.name)
                    }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(top = 8.dp)) {
                    LENGTHS.forEach { length ->
                        FilterChip(selected = minutes == length, onClick = { minutes = length }, label = { Text("$length min") })
                    }
                }
                TextButton(onClick = { onStart(null, null, true) }) { Text("Count up with the Quick Start profile instead") }
            }
        },
        confirmButton = { TextButton(onClick = { onStart(profileId, minutes, false) }, enabled = profileId != null) { Text("Start") } },
        dismissButton = { TextButton(onClick = onCancel) { Text("Cancel") } },
    )
}

/**
 * Today, while the Wi-Fi link isn't connected (ADR 42): the controlled PC's session as its sync file says, with Start
 * or End early sent through the files, the request still waiting for the PC's answer, and "Update AccountabiliBuddy on
 * <PC>" for a PC too old to answer.
 */
@Composable
fun PcFileCard(
    pcName: String,
    state: PcState,
    writtenAt: java.time.Instant,
    updateNeeded: String?,
    waiting: SyncAction?,
    clock: ClockFormat,
    onStart: (profileId: String?, minutes: Int?, countUp: Boolean) -> Unit,
    onEndEarly: () -> Unit,
    onCancelWaiting: () -> Unit,
) {
    var choosingStart by remember { mutableStateOf(false) }
    val zone = ZoneId.systemDefault()
    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            IconTitle("PC · $pcName", style = MaterialTheme.typography.titleMedium)
            SessionLine(state, clock)
            Text(
                "Through the sync files · as of ${clock.time(java.time.LocalDateTime.ofInstant(writtenAt, zone))}",
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            when {
                updateNeeded != null -> Text(updateNeeded, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.error)
                waiting != null -> {
                    val what = when (waiting.kind) {
                        SyncAction.START -> "Starting on $pcName"
                        SyncAction.END_EARLY_BEGIN -> "Asking $pcName to end early"
                        else -> "Ending the session on $pcName"
                    }
                    Text(
                        "$what… sent ${clock.time(java.time.LocalDateTime.ofInstant(waiting.at, zone))}; the PC answers when it next syncs.",
                        style = MaterialTheme.typography.bodyMedium,
                    )
                    TextButton(onClick = onCancelWaiting) { Text("Stop waiting") }
                }
                state.isActive -> OutlinedButton(onClick = onEndEarly) { Text("End early…") }
                else -> Button(onClick = { choosingStart = true }) { Text("Start on the PC…") }
            }

            if (choosingStart) {
                StartOnPcDialog(
                    state = state,
                    onStart = { profileId, minutes, countUp ->
                        choosingStart = false
                        onStart(profileId, minutes, countUp)
                    },
                    onCancel = { choosingStart = false },
                )
            }
        }
    }
}

/** The PC's own friction: its wait counts down here (from when the PC answered), then the reason unlocks End. */
@Composable
fun EndEarlyDialog(terms: EndEarlyTerms, onEnd: (String) -> Unit, onCancel: () -> Unit, sending: Boolean = false) {
    // One extra second, so the phone never asks before the PC's own clock says the wait is over. Through the sync files
    // the wait ends at the PC's allowed-from time, plus 3 seconds for the clocks' skew (ADR 42).
    fun secondsLeft(): Int = terms.allowedFrom?.let { allowed ->
        val millis = java.time.Duration.between(java.time.Instant.now(), allowed.plusSeconds(SKEW_SECONDS)).toMillis()
        ((millis + 999) / 1000).toInt().coerceAtLeast(0)
    } ?: (terms.waitSeconds + 1 - ((SystemClock.elapsedRealtime() - terms.receivedAtElapsed) / 1000).toInt()).coerceAtLeast(0)
    var left by remember(terms) { mutableIntStateOf(if (terms.waitSeconds == 0 && terms.allowedFrom == null) 0 else secondsLeft()) }
    var reason by remember(terms) { mutableStateOf("") }
    LaunchedEffect(terms) {
        while (left > 0) {
            delay(1_000)
            left = secondsLeft()
        }
    }

    AlertDialog(
        onDismissRequest = onCancel,
        title = { Text("End the PC's session early?") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                terms.note?.let { Text(it, style = MaterialTheme.typography.bodySmall) }
                Text(if (left > 0) "Wait ${left / 60}:${"%02d".format(left % 60)}, the PC's own wait." else "You can end it now.")
                if (terms.minReasonLength > 0) {
                    OutlinedTextField(
                        value = reason,
                        onValueChange = { reason = it },
                        label = { Text("Why? (at least ${terms.minReasonLength} characters)") },
                        enabled = left == 0,
                    )
                }
            }
        },
        confirmButton = {
            TextButton(onClick = { onEnd(reason.trim()) }, enabled = !sending && left == 0 && reason.trim().length >= terms.minReasonLength) {
                Text(if (sending) "Sending…" else "End session")
            }
        },
        dismissButton = { TextButton(onClick = onCancel) { Text("Keep going") } },
    )
}

/** Opened from the PC's QR code: compare the code with the PC's screen, then link. */
@Composable
fun PairingDialog(offer: PairingLink, busy: Boolean, onPair: () -> Unit, onCancel: () -> Unit, onPairWatch: (() -> Unit)? = null) {
    AlertDialog(
        onDismissRequest = { if (!busy) onCancel() },
        title = { Text("Link with ${offer.pcName}?") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Check that the PC shows this code:")
                Text(offer.displayCode, style = MaterialTheme.typography.headlineSmall)
                Text("Then this phone shows the PC's session. Your network only, encrypted.", style = MaterialTheme.typography.bodySmall)
                if (onPairWatch != null) {
                    // The watch reaches the PC over Wi-Fi when the phone isn't around; the code goes to it instead.
                    TextButton(onClick = onPairWatch, enabled = !busy) { Text("Pair my watch instead") }
                }
            }
        },
        confirmButton = { TextButton(onClick = onPair, enabled = !busy) { Text(if (busy) "Linking…" else "Link") } },
        dismissButton = { TextButton(onClick = onCancel, enabled = !busy) { Text("Cancel") } },
    )
}
