package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TimePicker
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.curbox.CurboxStatus
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.engine.SleepSchedule
import com.accountabilibuddy.companion.link.LinkStatus
import com.accountabilibuddy.companion.link.LinkedPc
import com.accountabilibuddy.companion.model.SleepSettings
import com.accountabilibuddy.companion.model.SleepTimes
import com.accountabilibuddy.shared.WatchLinkSettings
import com.accountabilibuddy.shared.WatchNode
import java.time.Duration
import java.time.LocalDate
import java.time.LocalTime

/** Import from the PC, sleep times, the timeline notification and bedtime reminder, Health Connect. */
@Composable
fun SettingsScreen(
    state: CompanionState,
    clock: ClockFormat,
    health: HealthStatus,
    importing: Boolean,
    onImport: () -> Unit,
    onClearImport: () -> Unit,
    onSaveSleep: (SleepSettings) -> Unit,
    onTimelineNotification: (Boolean) -> Unit,
    onBedtimeReminder: (Boolean) -> Unit,
    onConnectHealth: () -> Unit,
    onAddWidget: () -> Unit,
    curbox: CurboxStatus,
    onConnectCurbox: () -> Unit,
    pcStatus: LinkStatus = LinkStatus.Unlinked,
    pairedPc: LinkedPc? = null,
    onPastePairingLink: (String) -> Boolean = { false },
    onUnpairPc: () -> Unit = {},
    watch: WatchSectionState = WatchSectionState(),
    watchActions: WatchSectionActions = WatchSectionActions(),
) {
    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        item {
            Section("Profiles from the PC") {
                Text(
                    if (state.hasImport) "${state.importedFrom}; imported ${state.importedAt}." else "Nothing imported yet.",
                    style = MaterialTheme.typography.bodyMedium,
                )
                Text(
                    "On the PC: Settings → Backup → Export, without secrets or with a password. Open the .abbackup file here. The phone keeps it to itself.",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Button(onClick = onImport, enabled = !importing) { Text(if (importing) "Importing…" else "Import backup file") }
                    if (state.hasImport) {
                        OutlinedButton(onClick = onClearImport) { Text("Remove") }
                    }
                }
            }
        }

        item { HorizontalDivider() }
        item { SleepSection(state.sleep, clock, onSaveSleep) }
        item { HorizontalDivider() }

        item {
            Section("On the phone") {
                ToggleRow("Timeline notification", "Always on, silent, pinned near the top", state.timelineNotificationOn, onChange = onTimelineNotification)
                ToggleRow("Bedtime reminder", "Once a night, two hours before bedtime", state.bedtimeReminderOn, onChange = onBedtimeReminder)
                OutlinedButton(onClick = onAddWidget) { Text("Add timeline widget to home screen") }
                Text(
                    when (health) {
                        HealthStatus.Connected -> "Health Connect: reading sleep."
                        HealthStatus.NeedsPermission -> "Health Connect: not connected."
                        HealthStatus.NotAvailable -> "Health Connect isn't available on this phone."
                        HealthStatus.Checking -> "Health Connect: checking…"
                    },
                    style = MaterialTheme.typography.bodyMedium,
                )
                if (health == HealthStatus.NeedsPermission) {
                    OutlinedButton(onClick = onConnectHealth) { Text("Connect Health Connect") }
                }
            }
        }

        item { HorizontalDivider() }
        item { PcSection(pcStatus, pairedPc, onPastePairingLink, onUnpairPc) }

        item { HorizontalDivider() }
        item { WatchSection(watch, watchActions) }

        item { HorizontalDivider() }
        item { CurboxSection(curbox, onConnectCurbox) }

        item { HorizontalDivider() }
        item {
            Text(
                "Local only: the app talks only to your linked PC, on your own network and encrypted, and to your linked watch " +
                    "(through Google's encrypted relay only if you allow it above). Nothing else goes to the internet.",
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun CurboxSection(curbox: CurboxStatus, onConnect: () -> Unit) {
    Section("Curbox") {
        when {
            !curbox.installed -> Text(
                "Curbox isn't installed (or is too old for its API). AccountabiliBuddy works alongside it when it is.",
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            !curbox.granted -> {
                Text(
                    "Curbox is installed. Allow AccountabiliBuddy in Curbox to show its status and groups here (read only for now).",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                OutlinedButton(onClick = onConnect) { Text("Connect to Curbox") }
            }
            else -> {
                Text(
                    if (curbox.focusActive) "Focus running: ${curbox.focusGroup ?: ""}${curbox.focusRemainingMinutes?.let { ", $it min left" } ?: ""}" else "No Curbox focus running.",
                    style = MaterialTheme.typography.bodyMedium,
                )
                curbox.screenTimeMinutes?.let { Text("Screen time today (Curbox): ${it / 60}h ${it % 60}m", style = MaterialTheme.typography.bodySmall) }
                curbox.groups.forEach { group ->
                    Text(
                        "${group.kind}: ${group.name} · ${group.count} items${if (group.active == false) " · off" else ""}",
                        style = MaterialTheme.typography.bodySmall,
                    )
                }
                Text(
                    "Importing the groups' apps and sites needs Curbox's API to share them; today it only shares counts.",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        curbox.error?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = Color(0xFFFCA5A5)) }
    }
}

@Composable
private fun PcSection(status: LinkStatus, pc: LinkedPc?, onPaste: (String) -> Boolean, onUnpair: () -> Unit) {
    Section("PC") {
        if (pc == null) {
            Text(
                "Not linked. On the PC: Setup → Linked devices → Turn on → Pair a phone or tablet, then scan the QR code " +
                    "with this phone's camera. If the camera only shows the link as text, copy it and paste it here.",
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            var pasted by remember { mutableStateOf("") }
            var wrong by remember { mutableStateOf(false) }
            OutlinedTextField(
                value = pasted,
                onValueChange = {
                    pasted = it
                    wrong = false
                },
                label = { Text("Pairing link (accountabilibuddy://pair…)") },
                singleLine = true,
                isError = wrong,
                modifier = Modifier.fillMaxWidth(),
            )
            OutlinedButton(onClick = { wrong = !onPaste(pasted) }, enabled = pasted.isNotBlank()) { Text("Use this link") }
            if (wrong) {
                Text("That isn't a pairing link from the PC.", style = MaterialTheme.typography.bodySmall, color = Color(0xFFFCA5A5))
            }
            return@Section
        }

        val state = when (status) {
            is LinkStatus.Connected -> "connected"
            is LinkStatus.Offline -> "can't be reached right now"
            LinkStatus.Connecting -> "connecting…"
            else -> "connects while this app is open"
        }
        Text("Linked to ${pc.pcName} (${pc.host}) · $state", style = MaterialTheme.typography.bodyMedium)
        Text(
            "The PC's session shows on Today, where you can start one or end it early with the PC's own wait and reason.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        OutlinedButton(onClick = onUnpair) { Text("Unpair") }
    }
}

/** The Pixel Watch link (ADR 21) as Settings shows it. */
data class WatchSectionState(val settings: WatchLinkSettings = WatchLinkSettings(), val detected: List<WatchNode> = emptyList())

data class WatchSectionActions(
    val onFind: () -> Unit = {},
    val onLink: (WatchNode) -> Unit = {},
    val onUnlink: () -> Unit = {},
    val onDataLayer: (Boolean) -> Unit = {},
    val onRelay: (Boolean) -> Unit = {},
    val onWifi: (Boolean) -> Unit = {},
)

@Composable
private fun WatchSection(watch: WatchSectionState, actions: WatchSectionActions) {
    val settings = watch.settings
    LaunchedEffect(Unit) { actions.onFind() }

    Section("Pixel Watch") {
        val linkedName = settings.linkedName
        if (settings.linkedNodeId != null && linkedName != null) {
            Text("Linked: $linkedName. It shows today's plan, focus and the week as rings.", style = MaterialTheme.typography.bodyMedium)
            OutlinedButton(onClick = actions.onUnlink) { Text("Unlink") }
        } else if (watch.detected.isEmpty()) {
            Text(
                "No watch with AccountabiliBuddy found. Install the watch app, keep the watch connected to this phone, then check again.",
                style = MaterialTheme.typography.bodyMedium,
            )
            OutlinedButton(onClick = actions.onFind) { Text("Check again") }
        } else {
            watch.detected.forEach { found ->
                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                    Column(Modifier.weight(1f)) {
                        Text(found.name, style = MaterialTheme.typography.bodyLarge)
                        Text(if (found.isNearby) "Nearby" else "Not nearby right now", style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    Button(onClick = { actions.onLink(found) }) { Text("Link this watch") }
                }
            }
        }

        Text(
            "Nothing goes to a watch until you link it. Choose how it may get there, before linking or any time after:",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        ToggleRow("Through this phone", "Over the watch's own connection to the phone (Wear OS Data Layer).", settings.dataLayerOn, onChange = actions.onDataLayer)
        ToggleRow(
            "Google's encrypted relay",
            "When the watch is away from the phone's Bluetooth, Google's servers pass it along, end-to-end encrypted so Google can't " +
                "read it. Off: only while the watch is near the phone.",
            settings.relayOn && settings.dataLayerOn, enabled = settings.dataLayerOn, onChange = actions.onRelay,
        )
        ToggleRow(
            "Wi-Fi straight to the PC",
            "When the phone can't be reached, the watch asks the PC itself on your Wi-Fi (pair it from the PC's QR code with " +
                "\"Pair my watch\").",
            settings.wifiFallbackOn, onChange = actions.onWifi,
        )
        Text(
            "The watch gets plan parts, focus session times and profile names, drift times and totals. Never page titles, " +
                "addresses, reasons or notes.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun Section(title: String, content: @Composable () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(title, style = MaterialTheme.typography.titleMedium)
        content()
    }
}

@Composable
private fun ToggleRow(title: String, detail: String, checked: Boolean, enabled: Boolean = true, onChange: (Boolean) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.bodyLarge)
            Text(detail, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Switch(checked = checked, onCheckedChange = onChange, enabled = enabled)
    }
}

/** Which time the picker is editing. */
private enum class SleepField { WeeknightBed, WeeknightWake, WeekendBed, WeekendWake }

@Composable
private fun SleepSection(saved: SleepSettings, clock: ClockFormat, onSave: (SleepSettings) -> Unit) {
    var editing by remember { mutableStateOf<SleepField?>(null) }

    Section("Sleep") {
        Text(
            "The bar runs from wake time to bedtime and shades the night; from two hours before bedtime it says how much sleep you'll get if you go now. Comes from the PC: over the link when it's paired, or from a backup.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        ToggleRow("Sleep mode", "Bedtime and wake time on the timeline", saved.enabled) { onSave(saved.copy(enabled = it)) }
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

@Composable
private fun NightRow(label: String, times: SleepTimes, clock: ClockFormat, onBed: () -> Unit, onWake: () -> Unit) {
    val night = SleepSchedule(times, times).night(LocalDate.of(2026, 1, 5))
    val length = Duration.between(night.start, night.end)
    val suspicious = length < Duration.ofHours(3) || length > Duration.ofHours(16)
    Column {
        Text(label, style = MaterialTheme.typography.bodyMedium)
        Row(verticalAlignment = Alignment.CenterVertically) {
            OutlinedButton(onClick = onBed) { Text("Bed ${clock.time(LocalDate.now().atTime(times.bedtime))}") }
            Spacer(Modifier.width(8.dp))
            OutlinedButton(onClick = onWake) { Text("Up ${clock.time(LocalDate.now().atTime(times.wake))}") }
            Spacer(Modifier.width(10.dp))
            Text(
                if (suspicious) "⚠ ${DayBarBuilder.duration(length)}: check AM/PM" else "${DayBarBuilder.duration(length)} of sleep",
                color = if (suspicious) Color(0xFFFCA5A5) else MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.bodySmall,
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
        confirmButton = { TextButton(onClick = { onPick(LocalTime.of(picker.hour, picker.minute)) }) { Text("OK") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
        text = { TimePicker(state = picker) },
    )
}
