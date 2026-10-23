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
import kotlinx.coroutines.delay
import java.time.Duration
import java.time.OffsetDateTime
import java.time.ZoneId

private val LENGTHS = listOf(25, 50, 90)

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
                        Text(describe(state, clock), style = MaterialTheme.typography.bodyLarge)
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

/** The PC's own friction: its wait counts down here (from when the PC answered), then the reason unlocks End. */
@Composable
fun EndEarlyDialog(terms: EndEarlyTerms, onEnd: (String) -> Unit, onCancel: () -> Unit) {
    // One extra second, so the phone never asks before the PC's own clock says the wait is over.
    fun secondsLeft(): Int = (terms.waitSeconds + 1 - ((SystemClock.elapsedRealtime() - terms.receivedAtElapsed) / 1000).toInt()).coerceAtLeast(0)
    var left by remember(terms) { mutableIntStateOf(if (terms.waitSeconds == 0) 0 else secondsLeft()) }
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
            TextButton(onClick = { onEnd(reason.trim()) }, enabled = left == 0 && reason.trim().length >= terms.minReasonLength) { Text("End session") }
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
