package com.accountabilibuddy.companion.ui

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.IntentSenderRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Canvas
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
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.DriveFolderSource
import com.accountabilibuddy.companion.sync.DriveSyncSource
import com.accountabilibuddy.companion.sync.FolderStatus
import com.accountabilibuddy.companion.sync.PhoneSyncSettings
import com.accountabilibuddy.companion.sync.RelayAddress
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.shared.sync.FocusDayCombine
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.sync.SyncKeyCode
import com.accountabilibuddy.shared.sync.SyncState
import java.time.Duration
import java.time.Instant
import java.time.ZoneId
import kotlinx.coroutines.launch

/** What the Devices tab shows (ADR 22): this phone, the other devices and how fresh each is, and today across them. */
data class DevicesState(
    val deviceName: String,
    val joined: Boolean,
    val settings: PhoneSyncSettings,
    val received: List<ReceivedDevice>,
    /** The sync folder's last result (ADR 22, way B). */
    val folder: FolderStatus = FolderStatus(),
    /** Google Drive's last result (ADR 26). */
    val drive: FolderStatus = FolderStatus(),
    /** The own relay's last result (ADR 46). */
    val relay: FolderStatus = FolderStatus(),
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
    /** Starts the Drive sign-in: Google's screen to show, or null when nothing needs showing. */
    val beginDriveSignIn: suspend () -> android.app.PendingIntent? = { null },
    val onDriveSignInResult: (android.content.Intent?) -> Unit = {},
    val onSyncDriveNow: () -> Unit = {},
    val onSyncRelayNow: () -> Unit = {},
)

/**
 * The phone's Devices tab (ADR 22): this phone and every device in the group with a state pill, today's focus across
 * them, the sharing methods (safest first) and actions, then the Pixel Watch (moved from Settings).
 */
@Composable
fun DevicesScreen(
    devices: DevicesState,
    actions: DevicesActions,
    watch: WatchSectionState,
    watchActions: WatchSectionActions,
    /** The PCs whose files are here (sync device id to name, newest first), for picking the one to control (ADR 42). */
    pcChoices: List<Pair<String, String>> = emptyList(),
    /** The PC picked; null: the newest PC file's. */
    chosenPcId: String? = null,
    onChoosePc: (String?) -> Unit = {},
) {
    var dialog by remember { mutableStateOf<DevicesDialog?>(null) }
    val pickFolder = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri -> uri?.let(actions.onFolderChosen) }
    val now = Instant.now()
    val zone = ZoneId.systemDefault()

    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        item {
            Section("Your devices") {
                DevicesModel.todayLine(devices, now, zone)?.let { Text(it, style = MaterialTheme.typography.bodyMedium) }
                // This phone first, its linked watch indented under it, then the other devices.
                OwnDeviceRows.rows(devices.deviceName, devices.joined, watch.settings, watch.detected.map { it.id }.toSet())
                    .forEach { row -> DeviceRowView(row) }
                DevicesModel.rows(devices, now).forEach { row -> DeviceRowView(row) }
                if (devices.joined && devices.received.isEmpty()) {
                    Text("No other device yet.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Hint("Scan the PC's codes, open a sync file, or sign in to Google Drive below.", label = "How to bring them in")
                }
            }
        }

        // ADR 42: with several PCs, which one Today's PC card starts, ends early and sends plan edits to.
        if (pcChoices.size > 1) {
            item {
                val chosenName = pcChoices.firstOrNull { it.first == chosenPcId }?.second
                Section("PC this phone controls", summary = chosenName ?: "The newest: ${pcChoices.first().second}") {
                    Text(
                        "Start, end early and plan edits from Today go to this PC through the sync files.",
                        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    FlowChoices(
                        listOf(null to "The newest PC file's") + pcChoices.map { (id, name) -> id to name },
                        selected = chosenPcId?.takeIf { id -> pcChoices.any { it.first == id } },
                        onSelect = onChoosePc,
                    )
                }
            }
        }

        item {
            Section("Link this phone", summary = if (devices.joined) "In your device group" else "Not linked yet", startOpen = !devices.joined) {
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
            val methods = listOfNotNull(
                "QR".takeIf { devices.settings.qrOn },
                "files".takeIf { devices.settings.fileOn },
                "Drive".takeIf { devices.settings.driveOn },
                "relay".takeIf { devices.settings.relayOn },
                "folder".takeIf { devices.settings.folderOn },
            )
            Section("How this phone shares", summary = if (methods.isEmpty()) "Nothing on" else methods.joinToString(" · ")) {
                Hint(
                    "Safest first. Sync files are encrypted with your devices' link code; they hold focus times, profile names and " +
                        "sites, never page titles, addresses or reasons.",
                    label = "What's shared",
                )
                ToggleRow("QR codes", "Scan the PC's codes with the camera; nothing travels but the picture.", devices.settings.qrOn) {
                    actions.onSettings(devices.settings.copy(qrOn = it))
                }
                ToggleRow("Sync files", "Open or share a .absync file by hand (email, chat, a USB cable).", devices.settings.fileOn) {
                    actions.onSettings(devices.settings.copy(fileOn = it))
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = { dialog = DevicesDialog.Scan }, enabled = devices.joined && devices.settings.qrOn) { Text("Scan code") }
                    OutlinedButton(onClick = actions.onShare, enabled = devices.joined && devices.settings.fileOn) { Text("Share sync file") }
                }
                OutlinedButton(onClick = { dialog = DevicesDialog.Picture }, enabled = devices.joined && devices.settings.qrOn) {
                    Text("Show as picture")
                }
                DriveSection(devices, actions, now)
                RelaySection(devices, actions, now)
                FolderSection(devices, actions, now) { pickFolder.launch(null) }
            }
        }

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
                listOfNotNull(code?.let { joinLink(it, devices) }),
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

// The link another device scans to join: the code, this phone's name and, when the relay is on, its address and pin (ADR 46).
private fun joinLink(code: String, devices: DevicesState): String {
    val settings = devices.settings
    val relay = settings.relayUrl?.takeIf { settings.relayOn }?.let { url ->
        "&r=${Uri.encode(url)}" + (settings.relayPin?.let { "&p=${Uri.encode(it)}" } ?: "")
    }.orEmpty()
    return "accountabilibuddy://join?c=$code&n=${Uri.encode(devices.deviceName)}$relay"
}

/** Which app's alarms are the wake-up alarm: learned (calendar apps never count) or pinned. */
/**
 * Settings → Sleep → Wake-up alarm (moved from Devices): the phone's next alarm as the wake time (ADR 23), here and
 * on the PCs, and which app's alarms count.
 */
@Composable
internal fun WakeAlarmRows(
    settings: com.accountabilibuddy.companion.sync.PhoneSyncSettings,
    alarmApps: List<Pair<String, String>>,
    learnedAlarmApp: String?,
    onSettings: (com.accountabilibuddy.companion.sync.PhoneSyncSettings) -> Unit,
) {
    ToggleRow(
        "Next alarm as wake time",
        "Your next alarm (Clock, QR Alarm…) is your wake time here and on your PCs, which get it in this phone's sync file. It wins over a one-night change.",
        settings.alarmOn,
    ) { onSettings(settings.copy(alarmOn = it)) }
    if (settings.alarmOn) {
        AlarmAppPicker(settings.alarmApp, alarmApps, learnedAlarmApp) { app -> onSettings(settings.copy(alarmApp = app)) }
    }
}

@Composable
private fun AlarmAppPicker(pinned: String?, alarmApps: List<Pair<String, String>>, learnedAlarmApp: String?, onPick: (String?) -> Unit) {
    var open by remember { mutableStateOf(false) }
    fun name(app: String?) = alarmApps.firstOrNull { it.first == app }?.second ?: app ?: "none yet"
        val label = if (pinned != null) "Wake-up alarm: ${name(pinned)} (pinned)"
    else "Wake-up alarm: Auto · ${learnedAlarmApp?.let(::name) ?: "learning (any alarm but calendars)"}"
    Column {
        TextButton(onClick = { open = true }) { Text("$label ▾") }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            DropdownMenuItem(text = { Text("Auto (learn from my mornings)") }, onClick = { open = false; onPick(null) })
            alarmApps.filter { !it.first.contains("calendar", ignoreCase = true) }.forEach { (app, appName) ->
                DropdownMenuItem(text = { Text(appName) }, onClick = { open = false; onPick(app) })
            }
        }
    }
}

/** Google Drive (ADR 26): the app's own Drive folder, the one the PCs use; off until signed in. */
@Composable
private fun DriveSection(devices: DevicesState, actions: DevicesActions, now: Instant) {
    val settings = devices.settings
    val scope = rememberCoroutineScope()
    val ask = rememberLauncherForActivityResult(ActivityResultContracts.StartIntentSenderForResult()) { actions.onDriveSignInResult(it.data) }
    val signIn: () -> Unit = {
        scope.launch {
            actions.beginDriveSignIn()?.let { ask.launch(IntentSenderRequest.Builder(it).build()) }
        }
    }

    ToggleRow(
        "Google Drive",
        "Sync through the app's own Drive folder, the one your PCs use, at home, at work or on mobile data. Every 30 s " +
            "while a session runs or the timeline is pinned, else 15 min. The app sees only the files it made.",
        settings.driveOn,
        enabled = devices.joined,
    ) { on ->
        if (on) signIn() else actions.onSettings(settings.copy(driveOn = false))
    }
    // Open while Drive isn't on yet (or sign-in failed): the steps are what's usually missing.
    DriveSetupHelp(startOpen = false)
    if (!settings.driveOn) {
        return
    }

    val status = devices.drive
    Text(
        status.problem ?: status.lastSyncAt?.let { "Synced ${DevicesModel.ago(Duration.between(it, now))}" } ?: "Not synced yet.",
        style = MaterialTheme.typography.bodySmall,
        color = if (status.problem != null) Color(0xFFFCA5A5) else MaterialTheme.colorScheme.onSurfaceVariant,
    )
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        OutlinedButton(onClick = actions.onSyncDriveNow) { Text("Sync now") }
        if (status.problem == DriveSyncSource.SIGN_IN_AGAIN) {
            OutlinedButton(onClick = signIn) { Text("Sign in again") }
        }
    }
}

/** The short steps for the phone's own relay; the full guide is on the PC (Devices -> Own relay). */
private val RELAY_STEPS = """
    1. Run the relay on a PC, a home server or a rented server (the PC's Devices page has the steps and the full guide).
    2. Easiest: on a device that already uses the relay, show its link QR (Link a device) and scan it here: the address and pin come with it.
    3. Or type the relay's https address here, and its pin (43 characters) when the relay made its own certificate. No pin: the relay needs a normal certificate (a reverse proxy with Let's Encrypt).
    4. Switch it on. This phone checks the pin on every connection and shows a problem instead of connecting if it doesn't match.
    The relay stores only sealed files and your link code never reaches it. While a session runs or the timeline is pinned, a change arrives in about a second; otherwise this phone looks every 15 minutes.
""".trimIndent()

/** The user's own sealed relay (ADR 46): its https address and pin, and a switch. */
@Composable
private fun RelaySection(devices: DevicesState, actions: DevicesActions, now: Instant) {
    val settings = devices.settings
    var address by remember(settings.relayUrl) { mutableStateOf(settings.relayUrl.orEmpty()) }
    var pin by remember(settings.relayPin) { mutableStateOf(settings.relayPin.orEmpty()) }
    var error by remember { mutableStateOf<String?>(null) }

    // The fields as the settings would hold them, or null with the reason shown under them.
    fun checked(): PhoneSyncSettings? {
        val url = RelayAddress.normalize(address)
        if (url == null) {
            error = "Type the relay's address starting with https://, e.g. https://192.168.1.20:8743."
            return null
        }

        val typedPin = pin.trim()
        if (typedPin.isNotEmpty() && !RelayAddress.isPin(typedPin)) {
            error = "The pin is 43 letters, digits, - and _ (leave it empty for a relay with a normal certificate)."
            return null
        }

        error = null
        return settings.copy(relayUrl = url, relayPin = typedPin.ifEmpty { null })
    }

    ToggleRow(
        "Own relay",
        "Sync through a small relay you run yourself: changes arrive in about a second while a session runs or the " +
            "timeline is pinned, with no Google account. It only ever holds sealed files.",
        settings.relayOn,
        enabled = devices.joined,
    ) { on ->
        if (!on) {
            actions.onSettings(settings.copy(relayOn = false))
        } else {
            checked()?.let { actions.onSettings(it.copy(relayOn = true)) }
        }
    }
    OutlinedTextField(
        value = address,
        onValueChange = { address = it },
        label = { Text("Relay address (https://...)") },
        singleLine = true,
        modifier = Modifier.fillMaxWidth(),
    )
    OutlinedTextField(
        value = pin,
        onValueChange = { pin = it },
        label = { Text("Certificate pin (empty: normal certificate)") },
        singleLine = true,
        modifier = Modifier.fillMaxWidth(),
    )
    error?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = Color(0xFFFCA5A5)) }
    val changed = RelayAddress.normalize(address) != settings.relayUrl || pin.trim().ifEmpty { null } != settings.relayPin
    if (settings.relayOn && changed) {
        OutlinedButton(onClick = { checked()?.let { actions.onSettings(it) } }) { Text("Use this address and pin") }
    }
    Hint(RELAY_STEPS, label = "Sync through your own relay")
    if (!settings.relayOn) {
        return
    }

    val status = devices.relay
    Text(
        status.problem ?: status.lastSyncAt?.let { "Synced ${DevicesModel.ago(Duration.between(it, now))}" } ?: "Not synced yet.",
        style = MaterialTheme.typography.bodySmall,
        color = if (status.problem != null) Color(0xFFFCA5A5) else MaterialTheme.colorScheme.onSurfaceVariant,
    )
    OutlinedButton(onClick = actions.onSyncRelayNow) { Text("Sync now") }
}

/** The short steps for a sync folder kept in step through the user's own server; the full guide is on the PC. */
private val SELF_HOSTED_STEPS = """
    1. Install Syncthing-Fork here and Syncthing on your computers. Or, with Nextcloud, a two-way sync app such as Autosync for Nextcloud or FolderSync (Nextcloud's own auto upload only uploads).
    2. Connect the devices (Syncthing: Show ID on one, Add Remote Device on the other) and let the app ignore battery optimisation.
    3. Share one folder, e.g. "AccountabiliBuddy sync", as Send & Receive. On the phone keep it in internal storage, not the top of Download.
    4. Here: switch on Sync folder (backup) and pick that folder in Android's folder picker.
    5. On the PC: Devices → Sync folder → choose the same folder.
    6. Away from home, let Syncthing's relays or a mesh VPN (Tailscale, WireGuard) carry it. Never open a file share to the internet.
    The files are sealed, so the tool and any server only carry scrambled bytes. The full guide (always-on computer, safe access from outside, a small rented server): on the PC, Devices → Sync with your own server → Open the full guide.
""".trimIndent()

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
    Hint(SELF_HOSTED_STEPS, label = "Sync with your own server (Syncthing, Nextcloud)")
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
    Hint("On the PC: Devices → Link a new device, then scan its code with this phone's camera, or type the code here.", label = "Where's the code?")
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
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
        modifier = Modifier.fillMaxWidth().padding(start = if (row.indented) 28.dp else 0.dp),
    ) {
        DeviceKindIcon(row.kind)
        Column(Modifier.weight(1f)) {
            Text(row.name, style = MaterialTheme.typography.bodyLarge)
            Text(row.detail, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        // The state in words and colour, never colour alone.
        Box(
            Modifier
                .background(pillColor(row.pillTone), RoundedCornerShape(10.dp))
                .padding(horizontal = 8.dp, vertical = 2.dp),
        ) {
            Text(row.pillText, style = MaterialTheme.typography.labelSmall, color = Color.White)
        }
    }
}

/** A small outline of the device (PC, phone or watch), drawn from [DeviceKind.iconPath] in a 16 × 16 box. */
@Composable
private fun DeviceKindIcon(kind: DeviceKind) {
    val color = MaterialTheme.colorScheme.onSurfaceVariant
    val path = remember(kind) { PathParser().parsePathString(kind.iconPath).toPath() }
    Canvas(Modifier.size(22.dp)) {
        scale(size.minDimension / 16f, pivot = Offset.Zero) {
            drawPath(path, color, style = Stroke(width = 1.3f, cap = StrokeCap.Round, join = StrokeJoin.Round))
        }
    }
}

private fun pillColor(tone: PillTone): Color = when (tone) {
    PillTone.UP_TO_DATE -> Color(0xFF047857)
    PillTone.BEHIND -> Color(0xFFB45309)
    PillTone.WAITING -> Color(0xFF475569)
    PillTone.UNLINKED -> Color(0xFF7F1D1D)
    PillTone.THIS_DEVICE -> Color(0xFF0F766E)
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

/** The Devices tab's rows and totals line, from what the phone has (no Android here). */
object DevicesModel {
    /** How often another device's file should come when nothing else says (the PC's default interval). */
    val DEFAULT_INTERVAL: Duration = Duration.ofMinutes(15)

    fun rows(devices: DevicesState, now: Instant): List<DeviceRow> = devices.received
        .sortedByDescending { it.file.device.writtenAt }
        .map { device ->
            val card = device.file.device
            val age = Duration.between(card.writtenAt, now).coerceAtLeast(Duration.ZERO)
            val kind = when (card.kind) {
                SyncDeviceCard.KIND_PC -> "PC"
                SyncDeviceCard.KIND_WATCH -> "Watch"
                else -> "Phone"
            }
            val state = SyncState.of(age, DEFAULT_INTERVAL)
            DeviceRow(
                card.name,
                "$kind · ${ago(age)} · ${methodName(device.method)}",
                state.label,
                OwnDeviceRows.tone(state),
                DeviceKind.of(card.kind),
            )
        }

    /** E.g. "Today: 5h 10m focused across 3 devices"; null when there's nothing to add up. */
    fun todayLine(devices: DevicesState, now: Instant, zone: ZoneId): String? {
        val today = now.atZone(zone).toLocalDate()
        val combined = FocusDayCombine.combine(devices.received.map { it.file.device.name to it.file }, today, zone, now) ?: return null
        val count = combined.sessions.mapNotNull { it.device }.distinct().size
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
        SyncDeviceCard.METHOD_LINK -> "network link (old)"
        SyncDeviceCard.METHOD_FOLDER -> "sync folder"
        SyncDeviceCard.METHOD_QR -> "QR codes"
        SyncDeviceCard.METHOD_DRIVE -> "Google Drive"
        SyncDeviceCard.METHOD_RELAY -> "own relay"
        else -> "sync file"
    }
}

/** One chip per choice ([choices]: key to label), one under the other; [selected] is ticked. */
@Composable
private fun FlowChoices(choices: List<Pair<String?, String>>, selected: String?, onSelect: (String?) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
        choices.forEach { (key, label) ->
            FilterChip(selected = key == selected, onClick = { onSelect(key) }, label = { Text(label) })
        }
    }
}
