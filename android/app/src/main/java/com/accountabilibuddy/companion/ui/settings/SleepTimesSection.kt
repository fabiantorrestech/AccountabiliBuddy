package com.accountabilibuddy.companion.ui.settings

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Text
import androidx.compose.material3.TimePicker
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.engine.SleepSchedule
import com.accountabilibuddy.companion.model.SleepSettings
import com.accountabilibuddy.companion.model.SleepTimes
import com.accountabilibuddy.companion.ui.PollWhileVisible
import com.accountabilibuddy.companion.ui.Hint
import com.accountabilibuddy.companion.ui.Section
import com.accountabilibuddy.companion.ui.ToggleRow
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbValueButton
import com.accountabilibuddy.companion.ui.kit.ChangeWaitsSheet
import com.accountabilibuddy.companion.ui.kit.abTimePickerColors
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalTime

/** Which time the picker is editing. */
private enum class SleepField { WeeknightBed, WeeknightWake, WeekendBed, WeekendWake }

/** The sleep times card (Settings › Sleep): sleep mode with its wait to turn off, the every-night switch and the bed and wake times. */
@Composable
internal fun SleepSection(saved: SleepSettings, clock: ClockFormat, onSave: (SleepSettings) -> Unit) {
    var editing by remember { mutableStateOf<SleepField?>(null) }
    val app = LocalContext.current.applicationContext as CompanionApp
    val sleepSync = app.sleepSync
    var offText by remember { mutableStateOf(sleepSync.pendingOffText()) }
    var locked by remember { mutableStateOf(sleepSync.locked()) }
    // A pending off comes due, or a session starts, while the page is open.
    // Looked at every 2 s while a wait is pending, else every 10 s (the app applies a due off by itself; this is display only).
    PollWhileVisible(periodMs = { if (offText != null) 2_000L else 10_000L }) {
        if (sleepSync.applyPendingOffIfDue()) app.refreshSoon()
        offText = sleepSync.pendingOffText()
        locked = sleepSync.locked()
    }

    val summary = if (!saved.enabled) "Off" else "Bed ${clock.time(LocalDate.now().atTime(saved.weeknights.bedtime))} · up ${clock.time(LocalDate.now().atTime(saved.weeknights.wake))}"
    Section("Sleep times", summary = summary) {
        Hint("The bar runs from wake time to bedtime and shades the night; from two hours before bedtime it says how much sleep you'll get if you go now. Comes from the PC's sync file (Google Drive), or from a backup.")
        // On applies at once; off waits 10 minutes and is refused during a focus session (a pending off shows as off, with Cancel).
        val sleepDetail = when {
            offText != null -> offText!!
            locked && saved.enabled -> "Locked during a focus session"
            else -> "Bedtime and wake time on the timeline"
        }
        var asking by remember { mutableStateOf(false) }
        ToggleRow("Sleep mode", sleepDetail, saved.enabled && offText == null, enabled = !(locked && saved.enabled && offText == null)) { wantOn ->
            if (!wantOn) {
                // A looser change: it waits, then applies on its own (board 5h); the sheet asks before the wait starts.
                asking = true
            } else if (offText != null) {
                sleepSync.cancelPendingOff()
            } else {
                onSave(saved.copy(enabled = true))
            }

            offText = sleepSync.pendingOffText()
            locked = sleepSync.locked()
        }
        if (asking) {
            ChangeWaitsSheet(
                title = "Turn off sleep mode?",
                body = "This waits ${sleepSync.offWait.toMinutes()} minutes. If you still want it then, it turns off on its own.",
                appliesAt = Instant.now().plus(sleepSync.offWait),
                about = true,
                onQueue = {
                    onSave(saved.copy(enabled = false))
                    offText = sleepSync.pendingOffText()
                    locked = sleepSync.locked()
                },
                onDismiss = { asking = false },
            )
        }
        if (offText != null) {
            AbTextButton("Cancel", onClick = { sleepSync.cancelPendingOff(); offText = null })
        }
        ToggleRow("Same every night", "Off: Friday and Saturday nights use the weekend times", saved.sameEveryNight) { onSave(saved.copy(sameEveryNight = it)) }
        NightRow(if (saved.sameEveryNight) "Every night" else "Weeknights (Sun–Thu)", saved.weeknights, clock,
            onBed = { editing = SleepField.WeeknightBed }, onWake = { editing = SleepField.WeeknightWake })
        if (!saved.sameEveryNight) {
            NightRow("Weekend nights (Fri–Sat)", saved.weekends, clock,
                onBed = { editing = SleepField.WeekendBed }, onWake = { editing = SleepField.WeekendWake })
        }
    }

    editing?.let { field ->
        val initial = when (field) {
            SleepField.WeeknightBed -> saved.weeknights.bedtime
            SleepField.WeeknightWake -> saved.weeknights.wake
            SleepField.WeekendBed -> saved.weekends.bedtime
            SleepField.WeekendWake -> saved.weekends.wake
        }
        TimeDialog(initial, clock.is24Hour, onDismiss = { editing = null }) { picked ->
            editing = null
            onSave(
                when (field) {
                    SleepField.WeeknightBed -> saved.copy(weeknights = saved.weeknights.copy(bedtime = picked))
                    SleepField.WeeknightWake -> saved.copy(weeknights = saved.weeknights.copy(wake = picked))
                    SleepField.WeekendBed -> saved.copy(weekends = saved.weekends.copy(bedtime = picked))
                    SleepField.WeekendWake -> saved.copy(weekends = saved.weekends.copy(wake = picked))
                },
            )
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun NightRow(label: String, times: SleepTimes, clock: ClockFormat, onBed: () -> Unit, onWake: () -> Unit) {
    val night = SleepSchedule(times, times).night(LocalDate.of(2026, 1, 5))
    val length = Duration.between(night.start, night.end)
    val suspicious = length < Duration.ofHours(3) || length > Duration.ofHours(16)
    Column {
        AbSectionLabel(label)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            AbValueButton("Bed ${clock.time(LocalDate.now().atTime(times.bedtime))}", onClick = onBed)
            AbValueButton("Up ${clock.time(LocalDate.now().atTime(times.wake))}", onClick = onWake)
            Text(
                if (suspicious) "⚠ ${DayBarBuilder.duration(length)}: check AM/PM" else "${DayBarBuilder.duration(length)} of sleep",
                color = if (suspicious) Ab.colors.drift else Ab.colors.muted,
                style = Ab.type.small,
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TimeDialog(initial: LocalTime, is24Hour: Boolean, onDismiss: () -> Unit, onPick: (LocalTime) -> Unit) {
    val picker = rememberTimePickerState(initialHour = initial.hour, initialMinute = initial.minute, is24Hour = is24Hour)
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = Ab.colors.surface,
        confirmButton = { AbTextButton("OK", onClick = { onPick(LocalTime.of(picker.hour, picker.minute)) }) },
        dismissButton = { AbTextButton("Cancel", onClick = onDismiss) },
        text = { TimePicker(state = picker, colors = abTimePickerColors()) },
    )
}
