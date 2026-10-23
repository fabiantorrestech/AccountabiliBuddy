package com.accountabilibuddy.companion.ui

import android.Manifest
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.pause.PauseLength
import com.accountabilibuddy.companion.pause.PauseRules
import com.accountabilibuddy.companion.pause.PauseState
import com.accountabilibuddy.companion.pause.PauseStatus
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.ZoneId

/** The pause switch's actions. */
data class PauseActions(
    val onRequestOff: (PauseLength, Int, LocalTime) -> Unit = { _, _, _ -> },
    val onTurnOn: () -> Unit = {},
    val onCarPause: (Boolean) -> Unit = {},
    val onPlanOffDays: () -> Unit = {},
    /** The work-start nudge's repeat (5, 10, 15 minutes; 0 = once). */
    val nudgeRepeat: Int = 5,
    val onNudgeRepeat: (Int) -> Unit = {},
    /** The full-screen work nudge: on, and whether Android lets it show ("Display over other apps"). */
    val takeoverOn: Boolean = true,
    val takeoverAllowed: Boolean = false,
    val onTakeover: (Boolean) -> Unit = {},
    val onAllowTakeover: () -> Unit = {},
)

/**
 * Settings → "AccountabiliBuddy on this phone": the master switch (like Curbox's: turning it off waits first, then it
 * stays off for the chosen time) and "Pause in the car". Turning it back on is at once.
 */
@Composable
fun PauseSection(state: PauseState, now: Instant, clock: ClockFormat, actions: PauseActions) {
    val zone = ZoneId.systemDefault()
    // Shown as it is now, also once the wait ended before the next refresh applied it.
    val status = PauseRules.status(PauseRules.advance(state, now, zone), now, zone)
    var choosing by remember { mutableStateOf(false) }
    val summary = when (status) {
        PauseStatus.On -> if (state.carPause && state.inCar) "Paused in the car" else "On"
        is PauseStatus.Waiting -> "Turns off in ${left(status.left)}"
        is PauseStatus.Paused -> status.until?.let { "Off until ${clock.time(LocalDateTime.ofInstant(it, zone))}" } ?: "Off until you turn it on"
    }
    Section("AccountabiliBuddy on this phone", summary = summary, startOpen = status != PauseStatus.On) {
        Text(summary, style = MaterialTheme.typography.titleMedium, color = if (status == PauseStatus.On) Color.Unspecified else Color(0xFFF4A261))
        when (status) {
            PauseStatus.On -> OutlinedButton(onClick = { choosing = true }) { Text("Turn off…") }
            is PauseStatus.Waiting -> {
                Text("Nudges, the parked phone and pickups stop when the wait is over.", style = MaterialTheme.typography.bodySmall)
                Button(onClick = actions.onTurnOn) { Text("Cancel, keep it on") }
            }
            is PauseStatus.Paused -> Button(onClick = actions.onTurnOn) { Text("Turn back on") }
        }

        Text("Work-start nudge: again every", style = MaterialTheme.typography.bodyMedium)
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            listOf(5, 10, 15, 0).forEach { minutes ->
                FilterChip(
                    selected = actions.nudgeRepeat == minutes,
                    onClick = { actions.onNudgeRepeat(minutes) },
                    label = { Text(if (minutes == 0) "Once" else "$minutes min") },
                )
            }
        }

        ToggleRow(
            "Take over the screen",
            "During work time with no session, unlocking the phone shows a full-screen card: plan 3 tasks and start. " +
                "\"I'm starting\" gives 10 minutes, snoozing is free twice a day, then needs a reason.",
            actions.takeoverOn,
        ) { actions.onTakeover(it) }
        if (actions.takeoverOn && !actions.takeoverAllowed) {
            OutlinedButton(onClick = actions.onAllowTakeover) { Text("Allow \"Display over other apps\"") }
        }

        val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted -> actions.onCarPause(granted) }
        ToggleRow(
            "Pause in the car",
            "While the phone is connected to a car (its Bluetooth says car audio or hands-free, not just its name) or Android Auto runs: no nudges or parked phone, and unlocks don't count as pickups.",
            state.carPause,
        ) { on ->
            if (on && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) permission.launch(Manifest.permission.BLUETOOTH_CONNECT) else actions.onCarPause(on)
        }
        if (state.carPause && state.inCar) {
            Text("In the car now.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }

    if (choosing) {
        TurnOffDialog(
            onDismiss = { choosing = false },
            onPlanOffDays = {
                choosing = false
                actions.onPlanOffDays()
            },
            onConfirm = { length, wait, time ->
                choosing = false
                actions.onRequestOff(length, wait, time)
            },
        )
    }
}

@Composable
private fun TurnOffDialog(onDismiss: () -> Unit, onPlanOffDays: () -> Unit, onConfirm: (PauseLength, Int, LocalTime) -> Unit) {
    var length by remember { mutableStateOf(PauseLength.RestOfDay) }
    var time by remember { mutableStateOf(LocalTime.of(2, 0)) }
    var wait by remember { mutableIntStateOf(PauseRules.WAITS.first()) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Turn AccountabiliBuddy off?") },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("For how long?", style = MaterialTheme.typography.titleSmall)
                LengthRow("The rest of today (back on at midnight)", length == PauseLength.RestOfDay) { length = PauseLength.RestOfDay }
                LengthRow("Until a time tonight", length == PauseLength.UntilTime) { length = PauseLength.UntilTime }
                if (length == PauseLength.UntilTime) {
                    Row(horizontalArrangement = Arrangement.spacedBy(6.dp), modifier = Modifier.fillMaxWidth()) {
                        listOf(1, 2, 4, 6).forEach { hour ->
                            FilterChip(selected = time.hour == hour, onClick = { time = LocalTime.of(hour, 0) }, label = { Text("$hour AM") })
                        }
                    }
                }
                LengthRow("Until I turn it back on (remind me each morning)", length == PauseLength.UntilOnRemind) { length = PauseLength.UntilOnRemind }
                LengthRow("Until I turn it back on (no reminders)", length == PauseLength.UntilOnQuiet) { length = PauseLength.UntilOnQuiet }
                TextButton(onClick = onPlanOffDays) { Text("Plan off days in the day plan instead…") }
                Text("Wait before it turns off", style = MaterialTheme.typography.titleSmall)
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    PauseRules.WAITS.forEach { minutes ->
                        FilterChip(
                            selected = wait == minutes,
                            onClick = { wait = minutes },
                            label = { Text(if (minutes >= 24 * 60) "1 day" else if (minutes >= 60) "${minutes / 60} h" else "$minutes min") },
                        )
                    }
                }
            }
        },
        confirmButton = { Button(onClick = { onConfirm(length, wait, time) }) { Text("Start the wait") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Keep it on") } },
    )
}

@Composable
private fun LengthRow(label: String, selected: Boolean, onClick: () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        RadioButton(selected = selected, onClick = onClick)
        Text(label, style = MaterialTheme.typography.bodyMedium)
    }
}

/** "9:42" under an hour, else "23 h 10 m". */
private fun left(span: Duration): String {
    val seconds = span.seconds.coerceAtLeast(0)
    return if (seconds < 3600) "%d:%02d".format(seconds / 60, seconds % 60) else "${seconds / 3600} h ${seconds % 3600 / 60} m"
}
