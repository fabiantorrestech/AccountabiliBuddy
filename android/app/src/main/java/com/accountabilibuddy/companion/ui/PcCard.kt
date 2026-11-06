package com.accountabilibuddy.companion.ui

import android.os.SystemClock
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.EndEarlyTerms
import com.accountabilibuddy.companion.sync.PcState
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlinx.coroutines.delay

private val LENGTHS = listOf(25, 50, 90)

// The phone's and the PC's clocks may differ by a little: the phone sends a little after the wait.
private const val SKEW_SECONDS = 3L

/**
 * Today, when no PC file has reached this phone yet (ADR 45): the way to get one. Every PC-phone exchange goes through
 * Google Drive (or the sync folder, a share or QR codes), so there is no "Not linked" state.
 */
@Composable
fun PcMissingCard(onOpenDevices: () -> Unit) {
    AbCard {
        AbSectionLabel("PC")
        Text(
            "No PC has synced with this phone yet. Link one in Settings › Devices & sync to see and control it from here.",
            style = Ab.type.bodyMedium, color = Ab.colors.ink,
        )
        AbTonalButton("Devices & sync", onClick = onOpenDevices, modifier = Modifier.fillMaxWidth())
    }
}

/** Start on the controlled PC (ADR 42): the profile and the length, or the PC's own Quick Start. */
@Composable
internal fun StartOnPcDialog(state: PcState, pcName: String, onStart: (String?, Int?, Boolean) -> Unit, onCancel: () -> Unit) {
    var profileId by remember { mutableStateOf(state.profiles.firstOrNull()?.id) }
    var minutes by remember { mutableIntStateOf(LENGTHS.first()) }
    // With no profile to pick there is no plain Start (only the PC's own quick starts below), so the button is not offered.
    AbDialog(title = "Start on $pcName", onDismiss = onCancel, confirmText = "Start".takeIf { profileId != null }, onConfirm = { if (profileId != null) onStart(profileId, minutes, false) }) {
        if (state.profiles.isNotEmpty()) {
            AbSectionLabel("Profile")
            AbChipRow {
                state.profiles.forEach { profile -> AbChip(profile.name, selected = profileId == profile.id, onClick = { profileId = profile.id }) }
            }
        }
        AbSectionLabel("Length")
        AbChipRow { LENGTHS.forEach { length -> AbChip("$length min", selected = minutes == length, onClick = { minutes = length }) } }
        // No length: the PC runs its default profile for the length it remembers, counting down. Or explicitly with no set end.
        TextButton(onClick = { onStart(null, null, false) }) { Text("Quick Start with the PC's default profile", color = Ab.colors.ink) }
        TextButton(onClick = { onStart(null, null, true) }) { Text("No set end with the PC's default profile", color = Ab.colors.ink) }
    }
}

/**
 * The PC's own friction: its wait counts down here (from when the PC answered), and the reason can be typed meanwhile. During the
 * wait "Confirm" arms the end (ADR 66, [onArm]): the dialog closes and the phone sends it by itself when the wait is over; after
 * it, End sends it at once.
 */
@Composable
fun EndEarlyDialog(terms: EndEarlyTerms, onEnd: (String) -> Unit, onCancel: () -> Unit, sending: Boolean = false, onArm: ((String) -> Unit)? = null) {
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
        title = { Text("End ${terms.pcName ?: "the PC"}'s session early?") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                terms.note?.let { Text(it, style = androidx.compose.material3.MaterialTheme.typography.bodySmall) }
                Text(
                    when {
                        left == 0 -> "You can end it now."
                        onArm != null -> "Wait ${left / 60}:${"%02d".format(left % 60)}, the PC's own wait. Confirm to keep going: it ends by itself then."
                        else -> "Wait ${left / 60}:${"%02d".format(left % 60)}, the PC's own wait."
                    },
                )
                if (terms.minReasonLength > 0) {
                    OutlinedTextField(
                        value = reason,
                        onValueChange = { reason = it },
                        label = { Text("Why? (at least ${terms.minReasonLength} characters)") },
                        enabled = left == 0 || onArm != null,
                    )
                }
            }
        },
        confirmButton = {
            val reasonOk = reason.trim().length >= terms.minReasonLength
            if (left > 0 && onArm != null) {
                TextButton(onClick = { onArm(reason.trim()) }, enabled = !sending && reasonOk) {
                    Text("Confirm · ends in ${left / 60}:${"%02d".format(left % 60)}")
                }
            } else {
                TextButton(onClick = { onEnd(reason.trim()) }, enabled = !sending && left == 0 && reasonOk) {
                    Text(if (sending) "Sending…" else "End session")
                }
            }
        },
        dismissButton = { TextButton(onClick = onCancel) { Text("Keep going") } },
    )
}
