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
import com.accountabilibuddy.companion.sync.PendingSyncOff
import com.accountabilibuddy.companion.sync.SyncMethod
import com.accountabilibuddy.companion.sync.SyncOffWait
import com.accountabilibuddy.companion.sync.SyncReachText
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.ChangeWaitsSheet
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
    /** The held turn-off of the last way of syncing (10 minutes), or null: its row says when it turns off and offers Cancel. */
    val syncOffPending: PendingSyncOff? = null,
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
    /** A scanned or pasted join link (ADR 57): offers the join; false when the text isn't a join link. */
    val onJoinLink: (String) -> Boolean = { false },
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
    /** Cancels the held turn-off of the last way of syncing. */
    val onCancelSyncOff: () -> Unit = {},
    /** Whether turning a way off would wait 10 minutes (it is the only one that counts), so the sheet asks first. */
    val turnOffWaits: (SyncMethod) -> Boolean = { false },
    /** Whether pointing a way somewhere else is refused now (it is the only one that counts and has no problem). */
    val repointWaits: (SyncMethod) -> Boolean = { false },
    /** Whether a way counts as a way of syncing: it has brought another device's file (a way that has not can be changed at once). */
    val countsAsWay: (SyncMethod) -> Boolean = { false },
    /** Whether another device's file has come through a way from where it points now. */
    val hasDelivered: (SyncMethod) -> Boolean = { false },
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
    // The "nobody heard from" banner's button: scrolls to How they sync (the anchor stays, the count asks again).
    var showMethods by remember { mutableStateOf(0) }
    val pickFolder = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri -> uri?.let(actions.onFolderChosen) }
    val signIn = rememberDriveSignIn(actions)
    val relayFields = rememberRelayFields(settings)
    val lightSetting by UiPrefs.syncLight.collectAsState()
    val now = Instant.now()
    val zone = ZoneId.systemDefault()
    val detectedIds = page.watch.detected.map { it.id }.toSet()
    // During a focus session a way of syncing that is on can't be turned off or pointed elsewhere (turning one on stays allowed).
    val locked = devices.removalLocked
    // Turning off the LAST way that counts (on and unchanged for 10 minutes) waits 10 minutes (asked first, in a sheet); one of several turns off at once.
    var askOff by remember { mutableStateOf<SyncMethod?>(null) }
    val pendingOff = devices.syncOffPending
    fun requestOff(method: SyncMethod) {
        if (actions.turnOffWaits(method)) askOff = method else actions.onSettings(method.turnedOff(settings))
    }

    val rows = buildList {
        add(PageRow(SettingsAnchors.DEVICES) {
            DevicesCard(devices, DevicesModel.allRows(devices, detectedIds, page.watch.settings, now), DevicesModel.staleNote(devices, now, zone, hideWatchCards = page.watch.settings.linkedNodeId != null), DevicesModel.todayLine(devices, now, zone), actions, onShowMethods = { showMethods++ },
                // The last Sync now's full line, while the settings are still the ones it ran with.
                syncReport = page.syncNow.report?.takeIf { it.settings == settings }?.full)
        })

        add(PageRow(SettingsAnchors.HOW_THEY_SYNC) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                AbSectionLabel("How they sync", Modifier.padding(horizontal = 4.dp))
                locked?.let { SharingLockNote(it) }
                AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
                    // Each is its own switch: several may be on at once (the sync files are the same for all of them).
                    AbToggleRow(
                        "Google Drive", settings.driveOn,
                        { on -> if (on) signIn() else requestOff(SyncMethod.Drive) },
                        subtitle = "Works anywhere · every 30 s in a session, else 15 min",
                        enabled = devices.joined && !(settings.driveOn && locked != null) && pendingOff?.method != SyncMethod.Drive,
                    )
                    PendingOffRow(pendingOff, SyncMethod.Drive, zone, locked != null, actions.onCancelSyncOff)
                    AbToggleRow(
                        "Shared folder", settings.folderOn,
                        { on ->
                            if (!on) requestOff(SyncMethod.Folder)
                            else if (settings.folderUri == null) pickFolder.launch(null)
                            else actions.onSettings(settings.copy(folderOn = true))
                        },
                        subtitle = "A folder another app keeps in step (Autosync, Syncthing)",
                        enabled = devices.joined && !(settings.folderOn && locked != null) && pendingOff?.method != SyncMethod.Folder,
                    )
                    PendingOffRow(pendingOff, SyncMethod.Folder, zone, locked != null, actions.onCancelSyncOff)
                    AbToggleRow(
                        "Own relay", settings.relayOn,
                        { on ->
                            if (!on) requestOff(SyncMethod.Relay) else relayFields.checked(settings)?.let { actions.onSettings(it.copy(relayOn = true)) }
                        },
                        subtitle = "A relay you run · about a second in a session",
                        enabled = devices.joined && !(settings.relayOn && locked != null) && pendingOff?.method != SyncMethod.Relay,
                    )
                    PendingOffRow(pendingOff, SyncMethod.Relay, zone, locked != null, actions.onCancelSyncOff)
                }
                // Why the switch did not move: the relay's address, pin or key did not pass (shown here, where the switch is).
                relayFields.error?.let { Text(it, style = Ab.type.small, color = Ab.colors.drift, modifier = Modifier.padding(horizontal = 4.dp)) }
                Hint(
                    "Several can be on at once. Sync files are encrypted with your devices' link code; they hold focus times, " +
                        "profile names and sites, never page titles, addresses or reasons.",
                    label = "What's shared",
                )
            }
        })
        add(PageRow(SettingsAnchors.DRIVE) { DriveMethodCard(devices, actions, now, signIn) })
        add(PageRow(SettingsAnchors.FOLDER) { FolderMethodCard(devices, actions, now) { pickFolder.launch(null) } })
        add(PageRow(SettingsAnchors.RELAY) { RelayMethodCard(devices, actions, now, relayFields) })

        add(PageRow(SettingsAnchors.SYNC_RECEIPTS) {
            // For testing (developer): a notification when sync reaches each device, from the device cards' "seen"; off by default.
            val app = androidx.compose.ui.platform.LocalContext.current.applicationContext as com.accountabilibuddy.companion.CompanionApp
            var receiptsOn by remember { mutableStateOf(app.syncReceipts.isOn) }
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                AbSectionLabel("For testing", Modifier.padding(horizontal = 4.dp))
                AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
                    AbToggleRow(
                        "Tell me when sync reaches each device", receiptsOn,
                        { on -> app.syncReceipts.isOn = on; receiptsOn = on },
                        subtitle = "A quiet notification when another device's file shows it has this phone's latest one, and which don't yet. Every way of syncing; a watch can't confirm.",
                    )
                }
            }
        })
        add(PageRow(SettingsAnchors.BY_HAND) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                AbSectionLabel("By hand", Modifier.padding(horizontal = 4.dp))
                AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
                    AbToggleRow(
                        "QR codes", settings.qrOn, { actions.onSettings(settings.copy(qrOn = it)) },
                        subtitle = "Scan the PC's codes with the camera; nothing travels but the picture.", enabled = !(settings.qrOn && locked != null),
                    )
                    AbToggleRow(
                        "Sync files", settings.fileOn, { actions.onSettings(settings.copy(fileOn = it)) },
                        subtitle = "Open or share a .absync file by hand (email, chat, a USB cable).", enabled = !(settings.fileOn && locked != null),
                    )
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
                    JoinWithCode(actions.onJoin, actions.onJoinLink)
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
        SettingsPageList(
            rows,
            if (showMethods > 0) SettingsAnchors.HOW_THEY_SYNC else anchor,
            Modifier.weight(1f),
            scrollAgain = showMethods,
        )
        DevicesBottomBar(page.syncNow, canAdd = devices.joined, onAdd = { dialog = DevicesDialog.LinkDevice })
    }

    askOff?.let { method ->
        ChangeWaitsSheet(
            title = "Turn off ${method.label}?",
            body = "It is your last way of syncing, so this waits ${SyncOffWait.WAIT.toMinutes()} minutes. If you still want it then, it turns off on its own. " +
                "Until then this phone keeps syncing.",
            appliesAt = Instant.now().plus(SyncOffWait.WAIT),
            onQueue = { actions.onSettings(method.turnedOff(settings)) },
            onDismiss = { askOff = null },
        )
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

/** Under a way's switch while its turn-off waits: "Turns off at 4:05 PM" and Cancel (the way stays on until then). */
@Composable
private fun PendingOffRow(pending: PendingSyncOff?, method: SyncMethod, zone: ZoneId, inSession: Boolean, onCancel: () -> Unit) {
    if (pending?.method != method) return

    // A wait that ended in a focus session applies when the session ends.
    val text = if (inSession && !pending.at.isAfter(Instant.now())) "Turns off when your session ends" else "Turns off at ${DevicesModel.clockText(pending.at, zone)}"

    Row(Modifier.fillMaxWidth().padding(horizontal = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(text, style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.weight(1f))
        AbTextButton("Cancel", onClick = onCancel)
    }
}

/** Why the sharing switches that are on do not move during a focus session, as visible text (the PC says the same on its Devices page). */
@Composable
private fun SharingLockNote(reason: String) {
    Text(
        "Ways of syncing that are on can't be turned off or pointed somewhere else right now. $reason Turning one on still works.",
        style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 4.dp),
    )
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
private fun DevicesCard(devices: DevicesState, rows: List<DeviceRow>, stale: StaleNote?, todayLine: String?, actions: DevicesActions, onShowMethods: () -> Unit, syncReport: String? = null) {
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
        // Linked, but nobody heard from: what is missing (no way on, or a way on and nothing there yet), and the way to the switches.
        if (devices.joined && devices.received.isEmpty()) {
            AbInfoBanner(syncReport ?: SyncReachText.whileNobodyHeard(devices.settings), icon = Icons.Outlined.SyncProblem)
            AbTextButton("Show sync methods", onClick = onShowMethods)
            Hint("You can also scan the PC's codes or open a sync file.", label = "How to bring them in")
        } else {
            syncReport?.let { Text(it, style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 4.dp)) }
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
private fun JoinWithCode(onJoin: (String) -> Boolean, onJoinLink: (String) -> Boolean) {
    var code by remember { mutableStateOf("") }
    var wrong by remember { mutableStateOf(false) }
    Hint("On the PC: Settings › Devices & sync › Link a new device, then scan its code here, paste its link, or type the code.", label = "Where's the code?")
    JoinScanPasteRow(onLink = onJoinLink, onCode = { code = it; wrong = false })
    AbTextField(
        code, { code = it; wrong = false }, "Link code (40 characters)",
        capitalization = KeyboardCapitalization.None, isError = wrong,
    )
    AbPrimaryButton("Join", onClick = { wrong = !onJoin(code) }, enabled = code.isNotBlank(), modifier = Modifier.fillMaxWidth())
    if (wrong) {
        Text("That isn't a link code from your devices.", style = Ab.type.small, color = Ab.colors.drift)
    }
}
