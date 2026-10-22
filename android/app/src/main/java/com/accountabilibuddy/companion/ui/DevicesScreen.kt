package com.accountabilibuddy.companion.ui

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.link.LinkStatus
import com.accountabilibuddy.companion.link.LinkedPc
import com.accountabilibuddy.companion.sync.DriveFolderSource
import com.accountabilibuddy.companion.sync.FolderStatus
import com.accountabilibuddy.companion.sync.PhoneSyncSettings
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.shared.FocusDay
import com.accountabilibuddy.shared.sync.FocusDayCombine
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncKeyCode
import com.accountabilibuddy.shared.sync.SyncState
import java.time.Duration
import java.time.Instant
import java.time.ZoneId

/** What the Devices tab shows (ADR 22): this phone, the other devices and how fresh each is, and today across them. */
data class DevicesState(
    val deviceName: String,
    val joined: Boolean,
    val settings: PhoneSyncSettings,
    val received: List<ReceivedDevice>,
    val focusDay: FocusDay?,
    val pcName: String?,
    val pcConnected: Boolean,
    /** The sync folder's last result (ADR 22, way B). */
    val folder: FolderStatus = FolderStatus(),
    /** Apps seen setting alarms (id to name), and the one learned as the wake-up alarm. */
    val alarmApps: List<Pair<String, String>> = emptyList(),
    val learnedAlarmApp: String? = null,
)

/** The Devices tab's actions. */
data class DevicesActions(
    val onShare: () -> Unit = {},
    val ownQrLinks: () -> List<String> = { emptyList() },
    val linkCode: () -> String? = { null },
    val onJoin: (String) -> Boolean = { false },
    val onLeave: () -> Unit = {},
    val onSettings: (PhoneSyncSettings) -> Unit = {},
    /** A folder chosen with Android's folder picker (a tree uri). */
    val onFolderChosen: (Uri) -> Unit = {},
    val onSyncFolderNow: () -> Unit = {},
)

/**
 * The phone's Devices tab (ADR 22): this phone and every device in the group with a state pill, today's focus across
 * them, the sharing methods (safest first) and actions, then the PC link and the Pixel Watch (moved from Settings).
 */
@Composable
fun DevicesScreen(
    devices: DevicesState,
    actions: DevicesActions,
    pcStatus: LinkStatus,
    pairedPc: LinkedPc?,
    onPastePairingLink: (String) -> Boolean,
    onUnpairPc: () -> Unit,
    watch: WatchSectionState,
    watchActions: WatchSectionActions,
) {
    var dialog by remember { mutableStateOf<DevicesDialog?>(null) }
    val pickFolder = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri -> uri?.let(actions.onFolderChosen) }
    val now = Instant.now()
    val zone = ZoneId.systemDefault()

    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        item {
            Section("Your devices") {
                Text(
                    if (devices.joined) "${devices.deviceName} (this phone) · in your device group"
                    else "${devices.deviceName} (this phone) · not linked to your other devices yet",
                    style = MaterialTheme.typography.bodyLarge,
                )
                DevicesModel.todayLine(devices, now, zone)?.let { Text(it, style = MaterialTheme.typography.bodyMedium) }
                DevicesModel.rows(devices, now).forEach { row -> DeviceRowView(row) }
                if (devices.joined && devices.received.isEmpty()) {
                    Text(
                        "No other device's sync file yet: scan the PC's codes, open a sync file, or connect to the linked PC.",
                        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
        }

        item {
            Section("Link this phone") {
                if (devices.joined) {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        OutlinedButton(onClick = { dialog = DevicesDialog.LinkDevice }) { Text("Link a device") }
                        OutlinedButton(onClick = { dialog = DevicesDialog.Leave }) { Text("Leave group") }
                    }
                } else {
                    JoinWithCode(actions.onJoin)
                }
            }
        }

        item {
            Section("How this phone shares") {
                Text(
                    "Safest first. Sync files are encrypted with your devices' link code; they hold focus times, profile names and " +
                        "sites, never page titles, addresses or reasons.",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                ToggleRow("QR codes", "Scan the PC's codes with the camera; nothing travels but the picture.", devices.settings.qrOn) {
                    actions.onSettings(devices.settings.copy(qrOn = it))
                }
                ToggleRow("Sync files", "Open or share a .absync file by hand (email, chat, a USB cable).", devices.settings.fileOn) {
                    actions.onSettings(devices.settings.copy(fileOn = it))
                }
                ToggleRow("Network link", "With the paired PC, on your own network, encrypted.", devices.settings.linkOn) {
                    actions.onSettings(devices.settings.copy(linkOn = it))
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = { dialog = DevicesDialog.Scan }, enabled = devices.joined && devices.settings.qrOn) { Text("Scan code") }
                    OutlinedButton(onClick = actions.onShare, enabled = devices.joined && devices.settings.fileOn) { Text("Share sync file") }
                }
                OutlinedButton(onClick = { dialog = DevicesDialog.Picture }, enabled = devices.joined && devices.settings.qrOn) {
                    Text("Show as picture")
                }
                FolderSection(devices, actions, now) { pickFolder.launch(null) }
                ToggleRow(
                    "Next alarm as wake time",
                    "Your next alarm (Clock, QR Alarm…) is your wake time here and on your PCs, which get it in this phone's sync file. It wins over a one-night change.",
                    devices.settings.alarmOn,
                ) { actions.onSettings(devices.settings.copy(alarmOn = it)) }
                if (devices.settings.alarmOn) {
                    AlarmAppPicker(devices) { app -> actions.onSettings(devices.settings.copy(alarmApp = app)) }
                }
            }
        }

        item { HorizontalDivider() }
        item { PcSection(pcStatus, pairedPc, onPastePairingLink, onUnpairPc) }
        item { HorizontalDivider() }
        item { WatchSection(watch, watchActions) }
    }

    when (dialog) {
        DevicesDialog.Scan -> InfoDialog(
            "Scan the PC's codes",
            "On the PC, open the codes (\"Sync phone\" by the search box, or QR in the on-screen window's Ctrl+Alt details), then " +
                "point this phone's camera at each one. Each opens AccountabiliBuddy, which adds them up.",
        ) { dialog = null }
        DevicesDialog.Picture -> QrDialog(
            "This phone's sync file",
            "Take a screenshot and drop it on the PC's window (a PC without a camera reads it from the picture).",
            actions.ownQrLinks(),
        ) { dialog = null }
        DevicesDialog.LinkDevice -> {
            val code = actions.linkCode()
            QrDialog(
                "Link another device",
                code?.let { "Scan this on the other device, or type the code there: ${SyncKeyCode.display(it)}" } ?: "This phone isn't in a group.",
                listOfNotNull(code?.let { "accountabilibuddy://join?c=$it&n=${Uri.encode(devices.deviceName)}" }),
            ) { dialog = null }
        }
        DevicesDialog.Leave -> AlertDialog(
            onDismissRequest = { dialog = null },
            title = { Text("Leave your device group?") },
            text = { Text("This phone stops sharing, and what came from your other devices is removed from it.") },
            confirmButton = {
                TextButton(onClick = {
                    actions.onLeave()
                    dialog = null
                }) { Text("Leave") }
            },
            dismissButton = { TextButton(onClick = { dialog = null }) { Text("Cancel") } },
        )
        null -> Unit
    }
}

private enum class DevicesDialog { Scan, Picture, LinkDevice, Leave }

/** Which app's alarms are the wake-up alarm: learned (calendar apps never count) or pinned. */
@Composable
private fun AlarmAppPicker(devices: DevicesState, onPick: (String?) -> Unit) {
    var open by remember { mutableStateOf(false) }
    fun name(app: String?) = devices.alarmApps.firstOrNull { it.first == app }?.second ?: app ?: "none yet"
    val pinned = devices.settings.alarmApp
    val label = if (pinned != null) "Wake-up alarm: ${name(pinned)} (pinned)"
    else "Wake-up alarm: Auto · ${devices.learnedAlarmApp?.let(::name) ?: "learning (any alarm but calendars)"}"
    Column {
        TextButton(onClick = { open = true }) { Text("$label ▾") }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            DropdownMenuItem(text = { Text("Auto (learn from my mornings)") }, onClick = { open = false; onPick(null) })
            devices.alarmApps.filter { !it.first.contains("calendar", ignoreCase = true) }.forEach { (app, appName) ->
                DropdownMenuItem(text = { Text(appName) }, onClick = { open = false; onPick(app) })
            }
        }
    }
}

/** The sync folder (backup, off by default): a folder another app such as Autosync keeps in step with Google Drive. */
@Composable
private fun FolderSection(devices: DevicesState, actions: DevicesActions, now: Instant, onChoose: () -> Unit) {
    val settings = devices.settings
    ToggleRow(
        "Sync folder (backup)",
        "A folder another app keeps in step with Google Drive (e.g. Autosync), the one your PC uses. Every 15 min; " +
            "faster only while the timeline notification is pinned.",
        settings.folderOn,
        enabled = devices.joined,
    ) { on ->
        if (on && settings.folderUri == null) onChoose() else actions.onSettings(settings.copy(folderOn = on))
    }
    if (!settings.folderOn) {
        return
    }

    Text(
        settings.folderUri?.let { "Folder: ${DriveFolderSource.displayName(it)}" } ?: "No folder chosen yet.",
        style = MaterialTheme.typography.bodyMedium,
    )
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        listOf(1, 5, 15).forEach { minutes ->
            FilterChip(
                selected = settings.folderMinutes == minutes,
                onClick = { actions.onSettings(settings.copy(folderMinutes = minutes)) },
                label = { Text("$minutes min") },
            )
        }
    }
    val status = devices.folder
    Text(
        status.problem ?: status.lastSyncAt?.let { "Synced ${DevicesModel.ago(Duration.between(it, now))}" } ?: "Not synced yet.",
        style = MaterialTheme.typography.bodySmall,
        color = if (status.problem != null) Color(0xFFFCA5A5) else MaterialTheme.colorScheme.onSurfaceVariant,
    )
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        OutlinedButton(onClick = onChoose) { Text("Choose folder") }
        OutlinedButton(onClick = actions.onSyncFolderNow, enabled = settings.folderUri != null) { Text("Sync now") }
    }
}

@Composable
private fun JoinWithCode(onJoin: (String) -> Boolean) {
    var code by remember { mutableStateOf("") }
    var wrong by remember { mutableStateOf(false) }
    Text(
        "On the PC: Devices → Link a new device, then scan its code with this phone's camera, or type the code here.",
        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    OutlinedTextField(
        value = code,
        onValueChange = {
            code = it
            wrong = false
        },
        label = { Text("Link code (40 characters)") },
        singleLine = true,
        isError = wrong,
        modifier = Modifier.fillMaxWidth(),
    )
    Button(onClick = { wrong = !onJoin(code) }, enabled = code.isNotBlank()) { Text("Join") }
    if (wrong) {
        Text("That isn't a link code from your devices.", style = MaterialTheme.typography.bodySmall, color = Color(0xFFFCA5A5))
    }
}

@Composable
private fun DeviceRowView(row: DeviceRow) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.weight(1f)) {
            Text(row.name, style = MaterialTheme.typography.bodyLarge)
            Text(row.detail, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        // The state in words and colour, never colour alone.
        Box(
            Modifier
                .background(pillColor(row.state), RoundedCornerShape(10.dp))
                .padding(horizontal = 8.dp, vertical = 2.dp),
        ) {
            Text(row.state.label, style = MaterialTheme.typography.labelSmall, color = Color.White)
        }
    }
}

private fun pillColor(state: SyncState): Color = when (state) {
    SyncState.UP_TO_DATE -> Color(0xFF047857)
    SyncState.BEHIND -> Color(0xFFB45309)
    SyncState.WAITING -> Color(0xFF475569)
    SyncState.UNLINKED -> Color(0xFF7F1D1D)
}

@Composable
private fun InfoDialog(title: String, text: String, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = { Text(text) },
        confirmButton = { TextButton(onClick = onDismiss) { Text("OK") } },
    )
}

@Composable
private fun QrDialog(title: String, text: String, links: List<String>, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text(text)
                links.forEachIndexed { index, link ->
                    Image(QrPicture.render(link), contentDescription = "Code ${index + 1} of ${links.size}", modifier = Modifier.size(260.dp))
                    if (links.size > 1) {
                        Text("${index + 1} of ${links.size}", style = MaterialTheme.typography.labelSmall)
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text("Done") } },
    )
}

/** One device on the Devices tab: its name, kind, age and method, and its state. */
data class DeviceRow(val name: String, val detail: String, val state: SyncState)

/** The Devices tab's rows and totals line, from what the phone has (no Android here). */
object DevicesModel {
    /** How often another device's file should come when nothing else says (the PC's default interval). */
    val DEFAULT_INTERVAL: Duration = Duration.ofMinutes(15)

    fun rows(devices: DevicesState, now: Instant): List<DeviceRow> = devices.received
        .sortedByDescending { it.file.device.writtenAt }
        .map { device ->
            val card = device.file.device
            val age = Duration.between(card.writtenAt, now).coerceAtLeast(Duration.ZERO)
            val live = device.method == SyncDeviceCard.METHOD_LINK && devices.pcConnected && card.name == devices.pcName
            val kind = when (card.kind) {
                SyncDeviceCard.KIND_PC -> "PC"
                SyncDeviceCard.KIND_WATCH -> "Watch"
                else -> "Phone"
            }
            DeviceRow(
                card.name,
                "$kind · ${ago(age)} · ${methodName(device.method)}",
                if (live) SyncState.UP_TO_DATE else SyncState.of(age, DEFAULT_INTERVAL),
            )
        }

    /** E.g. "Today: 5h 10m focused across 3 devices"; null when there's nothing to add up. */
    fun todayLine(devices: DevicesState, now: Instant, zone: ZoneId): String? {
        val today = now.atZone(zone).toLocalDate()
        val combined = FocusDayCombine.combine(
            devices.focusDay, devices.pcName, devices.received.map { it.file.device.name to it.file }, today, zone, now,
        ) ?: return null
        val count = combined.sessions.mapNotNull { it.device ?: devices.pcName }.distinct().size
        val minutes = combined.focusedMinutes
        val time = if (minutes >= 60) "${minutes / 60}h ${"%02d".format(minutes % 60)}m" else "${minutes}m"
        return if (count > 1) "Today: $time focused across $count devices" else "Today: $time focused"
    }

    fun ago(age: Duration): String = when {
        age < Duration.ofMinutes(1) -> "just now"
        age < Duration.ofHours(1) -> "${age.toMinutes()} min ago"
        age < Duration.ofDays(1) -> "${age.toHours()} h ago"
        else -> "${age.toDays()} days ago"
    }

    private fun methodName(method: String): String = when (method) {
        SyncDeviceCard.METHOD_LINK -> "network link"
        SyncDeviceCard.METHOD_FOLDER -> "sync folder"
        SyncDeviceCard.METHOD_QR -> "QR codes"
        else -> "sync file"
    }
}
