package com.accountabilibuddy.companion.ui

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.IntentSenderRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.QrCode2
import androidx.compose.material.icons.outlined.SyncProblem
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.FolderStatus
import com.accountabilibuddy.companion.sync.PhoneSyncSettings
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.sync.RemovedDevice
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbChoiceRow
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbInfoBanner
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.AbToggleRow
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.settings.PageRow
import com.accountabilibuddy.companion.ui.settings.SettingsAnchors
import com.accountabilibuddy.companion.ui.settings.SettingsPageList
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.SyncKeyCode
import java.time.Instant
import java.time.ZoneId
import java.util.UUID
import kotlinx.coroutines.launch

/** What Devices & sync shows (ADR 22): this phone, the other devices and how fresh each is, and today across them. */
data class DevicesState(
    val deviceName: String,
    val joined: Boolean,
    val settings: PhoneSyncSettings,
    val received: List<ReceivedDevice>,
    /** Every device held, a kept removed one included (ADR 54): the day's combined focus counts their sessions as history. */
    val allReceived: List<ReceivedDevice> = received,
    /** The sync folder's last result (ADR 22, way B). */
    val folder: FolderStatus = FolderStatus(),
    /** Google Drive's last result (ADR 26). */
    val drive: FolderStatus = FolderStatus(),
    /** The own relay's last result (ADR 46). */
    val relay: FolderStatus = FolderStatus(),
    /** Apps seen setting alarms (id to name), and the one learned as the wake-up alarm. */
    val alarmApps: List<Pair<String, String>> = emptyList(),
    val learnedAlarmApp: String? = null,
    /** The devices removed from the group (ADR 54): the ones whose data is kept show as "(removed)". */
    val removed: List<RemovedDevice> = emptyList(),
    /** Why devices can't be removed right now (a focus session the user started); null when they can. */
    val removalLocked: String? = null,
    /** This phone's own app version, for its row ("AB 3.4.2 · …"); blank when not known. */
    val appVersion: String = "",
)

/** Devices & sync's actions. */
data class DevicesActions(
    /** "Remove this device…" on a device's row (ADR 54). */
    val onRemoveDevice: (UUID) -> Unit = {},
    /** "Restore from an older device…" (ADR 55). */
    val onRestore: () -> Unit = {},
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

private enum class DevicesDialog { Scan, Picture, LinkDevice, Leave }

/**
 * Settings › Devices & sync (board 5f, ADR 22): the devices in the group with how long ago each synced, a note when one is behind,
 * "How they sync" (each method is its own switch, so several can be on at once), each method's own settings, what is shared by hand,
 * the sync light setting, the PC this phone controls, linking, restoring, and the Pixel Watch. "Sync now" and a QR button to add a
 * device stay at the bottom. No Scaffold and no fixed width; [anchor] scrolls to a row (a search result).
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun DevicesScreen(page: DevicesPageInputs, anchor: String?) {
    val devices = page.devices
    val actions = page.actions
    val settings = devices.settings
    val context = LocalContext.current
    var dialog by remember { mutableStateOf<DevicesDialog?>(null) }
    val pickFolder = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri -> uri?.let(actions.onFolderChosen) }
    val signIn = rememberDriveSignIn(actions)
    val relayFields = rememberRelayFields(settings)
    val lightSetting by UiPrefs.syncLight.collectAsState()
    val now = Instant.now()
    val zone = ZoneId.systemDefault()
    val detectedIds = page.watch.detected.map { it.id }.toSet()

    val rows = buildList {
        add(PageRow(SettingsAnchors.DEVICES) {
            DevicesCard(devices, DevicesModel.allRows(devices, detectedIds, page.watch.settings, now), DevicesModel.staleNote(devices, now, zone), DevicesModel.todayLine(devices, now, zone), actions)
        })

        add(PageRow(SettingsAnchors.HOW_THEY_SYNC) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                AbSectionLabel("How they sync", Modifier.padding(horizontal = 4.dp))
                AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
                    // Each is its own switch: several may be on at once (the sync files are the same for all of them).
                    AbToggleRow(
                        "Google Drive", settings.driveOn,
                        { on -> if (on) signIn() else actions.onSettings(settings.copy(driveOn = false)) },
                        subtitle = "Works anywhere · every 30 s in a session, else 15 min", enabled = devices.joined,
                    )
                    AbToggleRow(
                        "Shared folder", settings.folderOn,
                        { on -> if (on && settings.folderUri == null) pickFolder.launch(null) else actions.onSettings(settings.copy(folderOn = on)) },
                        subtitle = "OneDrive, Dropbox or a NAS path · every ${settings.folderMinutes} min", enabled = devices.joined,
                    )
                    AbToggleRow(
                        "Own relay", settings.relayOn,
                        { on ->
                            if (!on) actions.onSettings(settings.copy(relayOn = false)) else relayFields.checked(settings)?.let { actions.onSettings(it.copy(relayOn = true)) }
                        },
                        subtitle = "A relay you run · about a second in a session", enabled = devices.joined,
                    )
                }
                Hint(
                    "Several can be on at once. Safest first. Sync files are encrypted with your devices' link code; they hold focus times, " +
                        "profile names and sites, never page titles, addresses or reasons.",
                    label = "What's shared",
                )
            }
        })
        add(PageRow(SettingsAnchors.DRIVE) { DriveMethodCard(devices, actions, now, signIn) })
        add(PageRow(SettingsAnchors.FOLDER) { FolderMethodCard(devices, actions, now) { pickFolder.launch(null) } })
        add(PageRow(SettingsAnchors.RELAY) { RelayMethodCard(devices, actions, now, relayFields) })

        add(PageRow(SettingsAnchors.BY_HAND) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                AbSectionLabel("By hand", Modifier.padding(horizontal = 4.dp))
                AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
                    AbToggleRow("QR codes", settings.qrOn, { actions.onSettings(settings.copy(qrOn = it)) }, subtitle = "Scan the PC's codes with the camera; nothing travels but the picture.")
                    AbToggleRow("Sync files", settings.fileOn, { actions.onSettings(settings.copy(fileOn = it)) }, subtitle = "Open or share a .absync file by hand (email, chat, a USB cable).")
                    FlowRow(Modifier.padding(vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        AbTonalButton("Scan code", onClick = { dialog = DevicesDialog.Scan }, enabled = devices.joined && settings.qrOn)
                        AbTonalButton("Share sync file", onClick = actions.onShare, enabled = devices.joined && settings.fileOn)
                        AbTonalButton("Show as picture", onClick = { dialog = DevicesDialog.Picture }, enabled = devices.joined && settings.qrOn)
                    }
                }
            }
        })

        add(PageRow(SettingsAnchors.SYNC_LIGHT) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                AbSectionLabel("Show the sync light", Modifier.padding(horizontal = 4.dp))
                AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
                    SyncLightSetting.entries.forEach { choice ->
                        AbChoiceRow(choice.label, selected = lightSetting == choice, onClick = { UiPrefs.setSyncLight(context, choice) })
                    }
                }
                Text(
                    "The light is the dot and device icons next to the date on Today. This only hides it: syncing and the ↻ button work the same.",
                    style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 4.dp),
                )
            }
        })

        // ADR 42: with several PCs, which one Today's PC card starts, ends early and sends plan edits to.
        if (page.pcChoices.size > 1) {
            add(PageRow(SettingsAnchors.PC_CONTROL) {
                val chosen = page.chosenPcId?.takeIf { id -> page.pcChoices.any { it.first == id } }
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    AbSectionLabel("PC this phone controls", Modifier.padding(horizontal = 4.dp))
                    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 8.dp), verticalSpacing = 0.dp) {
                        Text(
                            "Start, end early and plan edits from Today go to this PC through the sync files.",
                            style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(vertical = 4.dp),
                        )
                        AbChoiceRow("The newest PC file's", selected = chosen == null, subtitle = page.pcChoices.first().second, onClick = { page.onChoosePc(null) })
                        page.pcChoices.forEach { (id, name) ->
                            AbChoiceRow(name, selected = chosen == id, onClick = { page.onChoosePc(id) })
                        }
                    }
                }
            })
        }

        add(PageRow(SettingsAnchors.LINK) {
            AbCard(verticalSpacing = 8.dp) {
                AbSectionLabel("Link this phone")
                Text(if (devices.joined) "In your device group" else "Not linked yet", style = Ab.type.bodyMedium, color = Ab.colors.ink)
                if (devices.joined) {
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        AbTonalButton("Link a device", onClick = { dialog = DevicesDialog.LinkDevice })
                        AbTonalButton("Leave group", onClick = { dialog = DevicesDialog.Leave })
                    }
                } else {
                    JoinWithCode(actions.onJoin)
                }
            }
        })

        // ADR 55: a removed device's backup brings its history back (the setup assistant gets this step in a later part).
        if (devices.joined) {
            add(PageRow(SettingsAnchors.RESTORE) {
                AbCard(verticalSpacing = 8.dp) {
                    AbSectionLabel("Restore from an older device")
                    Text(
                        "A backup is a sealed file made when a device was removed, on Google Drive (Backups) or in a file you saved. " +
                            "Restoring it brings that device's history back under its old name, marked (removed).",
                        style = Ab.type.small, color = Ab.colors.muted,
                    )
                    AbTonalButton("Restore from an older device…", onClick = actions.onRestore)
                }
            })
        }

        add(PageRow(SettingsAnchors.WATCH) { WatchSection(page.watch, page.watchActions) })
    }

    Column(Modifier.fillMaxSize()) {
        SettingsPageList(rows, anchor, Modifier.weight(1f))
        DevicesBottomBar(page.syncNow, canAdd = devices.joined, onAdd = { dialog = DevicesDialog.LinkDevice })
    }

    when (dialog) {
        DevicesDialog.Scan -> InfoSheet(
            "Scan the PC's codes",
            "On the PC, open the codes (\"Sync phone\" by the search box, or QR in the on-screen window's Ctrl+Alt details), then " +
                "point this phone's camera at each one. Each opens AccountabiliBuddy, which adds them up.",
        ) { dialog = null }
        DevicesDialog.Picture -> QrSheet(
            "This phone's sync file",
            "Take a screenshot and drop it on the PC's window (a PC without a camera reads it from the picture).",
            actions.ownQrLinks(),
        ) { dialog = null }
        DevicesDialog.LinkDevice -> {
            val code = actions.linkCode()
            QrSheet(
                "Link another device",
                code?.let { "Scan this on the other device, or type the code there: ${SyncKeyCode.display(it)}. Never photograph, screen-share or post it: it is the key to all your synced data." } ?: "This phone isn't in a group.",
                listOfNotNull(code?.let { joinLink(it, devices) }),
            ) { dialog = null }
        }
        DevicesDialog.Leave -> AbDialog(
            title = "Leave your device group?",
            onDismiss = { dialog = null },
            confirmText = "Leave",
            onConfirm = {
                actions.onLeave()
                dialog = null
            },
        ) {
            Text("This phone stops sharing, and what came from your other devices is removed from it.", style = Ab.type.bodyMedium, color = Ab.colors.ink)
        }
        null -> Unit
    }
}

/** The signed-in dance for Drive (ADR 26): asks Google's screen to show, then hands its answer back. */
@Composable
private fun rememberDriveSignIn(actions: DevicesActions): () -> Unit {
    val scope = rememberCoroutineScope()
    val ask = rememberLauncherForActivityResult(ActivityResultContracts.StartIntentSenderForResult()) { actions.onDriveSignInResult(it.data) }
    return {
        scope.launch {
            actions.beginDriveSignIn()?.let { ask.launch(IntentSenderRequest.Builder(it).build()) }
        }
    }
}

/** The list of devices (board 5f): one card with a row each, then the note about a device that is behind. */
@Composable
private fun DevicesCard(devices: DevicesState, rows: List<DeviceRow>, stale: StaleNote?, todayLine: String?, actions: DevicesActions) {
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        AbCard(contentPadding = PaddingValues(horizontal = 10.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
            rows.forEachIndexed { index, row ->
                DeviceRowView(row, divider = index < rows.lastIndex, removalLocked = devices.removalLocked, onRemove = actions.onRemoveDevice)
            }
        }
        stale?.let { note ->
            AbInfoBanner(
                buildAnnotatedString {
                    withStyle(SpanStyle(fontWeight = FontWeight.Bold)) { append(note.name) }
                    append(note.rest)
                },
                icon = Icons.Outlined.SyncProblem,
            )
        }
        todayLine?.let { Text(it, style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 4.dp)) }
        devices.removalLocked?.let {
            Text("Removing a device: $it", style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 4.dp))
        }
        if (devices.joined && devices.received.isEmpty()) {
            Text("No other device yet.", style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 4.dp))
            Hint("Scan the PC's codes, open a sync file, or sign in to Google Drive below.", label = "How to bring them in")
        }
    }
}

/** The two buttons that stay at the bottom: a QR button to add a device, and Sync now. */
@Composable
private fun DevicesBottomBar(syncNow: SyncNow, canAdd: Boolean, onAdd: () -> Unit) {
    val colors = Ab.colors
    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        Surface(
            onClick = onAdd,
            enabled = canAdd,
            shape = CircleShape,
            color = colors.surface2,
            contentColor = if (canAdd) colors.ink else colors.faint,
            modifier = Modifier.size(52.dp).semantics { contentDescription = if (canAdd) "Add a device" else "Add a device (link this phone first)" },
        ) {
            Box(contentAlignment = Alignment.Center) { Icon(Icons.Outlined.QrCode2, contentDescription = null, modifier = Modifier.size(24.dp)) }
        }
        AbTonalButton(if (syncNow.refreshing) "Syncing…" else "Sync now", onClick = syncNow::run, enabled = !syncNow.refreshing, modifier = Modifier.weight(1f))
    }
}

// The link another device scans to join: the code, this phone's name and, when the relay is on, its address, pin and
// admission key (ADR 46). The whole link is a secret: whoever has it can read and write the group's files.
private fun joinLink(code: String, devices: DevicesState): String {
    val settings = devices.settings
    val relay = settings.relayUrl?.takeIf { settings.relayOn }?.let { url ->
        "&r=${Uri.encode(url)}" + (settings.relayPin?.let { "&p=${Uri.encode(it)}" } ?: "") +
            (settings.relayAdmission?.let { "&k=${Uri.encode(it)}" } ?: "")
    }.orEmpty()
    return "accountabilibuddy://join?c=$code&n=${Uri.encode(devices.deviceName)}$relay"
}

@Composable
private fun JoinWithCode(onJoin: (String) -> Boolean) {
    var code by remember { mutableStateOf("") }
    var wrong by remember { mutableStateOf(false) }
    Hint("On the PC: Settings › Devices & sync › Link a new device, then scan its code with this phone's camera, or type the code here.", label = "Where's the code?")
    AbTextField(
        code, { code = it; wrong = false }, "Link code (40 characters)",
        capitalization = KeyboardCapitalization.None, isError = wrong,
    )
    AbPrimaryButton("Join", onClick = { wrong = !onJoin(code) }, enabled = code.isNotBlank(), modifier = Modifier.fillMaxWidth())
    if (wrong) {
        Text("That isn't a link code from your devices.", style = Ab.type.small, color = Ab.colors.drift)
    }
}
