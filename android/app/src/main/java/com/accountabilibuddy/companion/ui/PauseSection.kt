package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.pause.PauseLength
import com.accountabilibuddy.companion.pause.PauseRules
import com.accountabilibuddy.companion.pause.PauseState
import com.accountabilibuddy.companion.pause.PauseStatus
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.kit.AbChoiceRow
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.theme.Ab
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
 * Settings → Guards → Phone guard: the master switch "AccountabiliBuddy on this phone" (like Curbox's: turning it off waits first,
 * then it stays off for the chosen time). Turning it back on is at once. Pause in the car and the work-start nudge are in
 * [PauseAdvancedControls].
 */
@Composable
fun PauseSection(state: PauseState, now: Instant, clock: ClockFormat, actions: PauseActions) =
    GuardsScope { PauseSectionBody(state, now, clock, actions) }

/**
 * While a focus session runs the page is view-only (the guard lock): a "turn off" request that is waiting shows as it is and is not
 * applied until the session is over, and nothing here can be changed.
 */
@Composable
private fun PauseSectionBody(state: PauseState, now: Instant, clock: ClockFormat, actions: PauseActions) {
    val zone = ZoneId.systemDefault()
    val resting = LocalGuardsLock.current != null
    // Shown as it is now, also once the wait ended before the next refresh applied it (not during a session: it waits).
    val status = PauseRules.status(PauseRules.advance(state, now, zone, sessionRunning = resting), now, zone)
    var choosing by remember { mutableStateOf(false) }
    val summary = when (status) {
        PauseStatus.On -> if (state.carPause && state.inCar) "Paused in the car" else "On"
        is PauseStatus.Waiting -> if (resting && status.left.isZero) "Turns off when your session ends" else "Turns off in ${left(status.left)}"
        is PauseStatus.Paused -> status.until?.let { "Off until ${clock.time(LocalDateTime.ofInstant(it, zone))}" } ?: "Off until you turn it on"
    }
    AbCard(contentPadding = PaddingValues(horizontal = 16.dp, vertical = 14.dp), verticalSpacing = 8.dp) {
        IconTitle("${com.accountabilibuddy.companion.guidance.appBrandName()} on this phone", style = Ab.type.body, icon = SectionIcons.of("AccountabiliBuddy on this phone"), fontWeight = FontWeight.SemiBold)
        GuardsRestNote()
        // Off or waiting to be off is the waiting tone: it is a state to notice, not an error.
        Text(summary, style = Ab.type.section, color = if (status == PauseStatus.On) Ab.colors.ink else Ab.colors.waiting)
        when (status) {
            PauseStatus.On -> GuardTonalButton("Turn off…", onClick = { choosing = true })
            is PauseStatus.Waiting -> {
                Note("Nudges, the parked phone and pickups stop when the wait is over.")
                GuardTonalButton("Cancel, keep it on", onClick = actions.onTurnOn)
            }
            is PauseStatus.Paused -> GuardTonalButton("Turn back on", onClick = actions.onTurnOn)
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
    AbDialog(
        title = "Turn ${com.accountabilibuddy.companion.guidance.appBrandName()} off?",
        onDismiss = onDismiss,
        confirmText = "Start the wait",
        onConfirm = { onConfirm(length, wait, time) },
        dismissText = "Keep it on",
    ) {
        Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            AbSectionLabel("For how long?")
            AbChoiceRow("The rest of today (back on at midnight)", length == PauseLength.RestOfDay) { length = PauseLength.RestOfDay }
            AbChoiceRow("Until a time tonight", length == PauseLength.UntilTime) { length = PauseLength.UntilTime }
            if (length == PauseLength.UntilTime) {
                AbChipRow {
                    listOf(1, 2, 4, 6).forEach { hour ->
                        AbChip("$hour AM", selected = time.hour == hour, onClick = { time = LocalTime.of(hour, 0) })
                    }
                }
            }
            AbChoiceRow("Until I turn it back on (remind me each morning)", length == PauseLength.UntilOnRemind) { length = PauseLength.UntilOnRemind }
            AbChoiceRow("Until I turn it back on (no reminders)", length == PauseLength.UntilOnQuiet) { length = PauseLength.UntilOnQuiet }
            AbTextButton("Plan off days in the day plan instead…", onClick = onPlanOffDays)
            AbSectionLabel("Wait before it turns off")
            AbChipRow {
                PauseRules.WAITS.forEach { minutes ->
                    AbChip(
                        if (minutes >= 24 * 60) "1 day" else if (minutes >= 60) "${minutes / 60} h" else "$minutes min",
                        selected = wait == minutes,
                        onClick = { wait = minutes },
                    )
                }
            }
        }
    }
}

/** "9:42" under an hour, else "23 h 10 m". */
private fun left(span: Duration): String {
    val seconds = span.seconds.coerceAtLeast(0)
    return if (seconds < 3600) "%d:%02d".format(seconds / 60, seconds % 60) else "${seconds / 3600} h ${seconds % 3600 / 60} m"
}
