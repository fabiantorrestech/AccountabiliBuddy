package com.accountabilibuddy.companion.debug

import android.content.Intent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.DriveBackupInfo
import com.accountabilibuddy.companion.sync.PhoneSyncSettings
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.ui.DevicesActions
import com.accountabilibuddy.companion.ui.DevicesPageInputs
import com.accountabilibuddy.companion.ui.DevicesScreen
import com.accountabilibuddy.companion.ui.DevicesState
import com.accountabilibuddy.companion.ui.RemoveBackupChoice
import com.accountabilibuddy.companion.ui.RemoveDeviceFlow
import com.accountabilibuddy.companion.ui.RemoveDeviceSheet
import com.accountabilibuddy.companion.ui.RemoveDeviceState
import com.accountabilibuddy.companion.ui.RemoveStep
import com.accountabilibuddy.companion.ui.RestoreBackupFlow
import com.accountabilibuddy.companion.ui.RestoreBackupSheet
import com.accountabilibuddy.companion.ui.RestoreState
import com.accountabilibuddy.companion.ui.WatchSectionActions
import com.accountabilibuddy.companion.ui.WatchSectionState
import com.accountabilibuddy.companion.ui.rememberSyncNow
import com.accountabilibuddy.companion.ui.settings.SettingsPageFrame
import com.accountabilibuddy.companion.ui.SettingsCategory
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.WatchLinkSettings
import com.accountabilibuddy.shared.WatchNode
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import java.time.Duration
import java.time.Instant
import java.util.UUID

/**
 * Debug only: Settings > Devices & sync over made-up devices (a PC just now, a tablet three hours behind, a linked watch), and the
 * remove and restore sheets in every state, for screenshots on the emulator. Opened through [KitGalleryActivity]:
 * `--es show devices [--es anchor relay] [--es relay on|off] [--ez nowatch true] [--ez locked true] [--es pending relay|folder|drive [--ez due true]]`
 * (a turn-off of the last way of syncing that waits; `due` = its time has passed, in a session), `--es show remove --es step what|data|backup|older|confirm|working|problem|done`
 * (`--ez locked true` adds the session lock), `--es show restore [--es mode list|empty|problem]`.
 */
@Composable
fun DevicesGallery(intent: Intent) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    when (intent.getStringExtra("show")) {
        "remove" -> {
            val locked = intent.getBooleanExtra("locked", false)
            val step = RemoveStep.entries.firstOrNull { it.name.equals(intent.getStringExtra("step"), ignoreCase = true) } ?: RemoveStep.What
            val flow = remember { RemoveDeviceFlow(app, app.sync, com.accountabilibuddy.companion.sync.DriveBackups(app.driveSignIn), scope) }
            val state = RemoveDeviceState(
                deviceId = UUID.randomUUID(), name = "Galaxy Tab", isPhone = true, driveAvailable = true, suggestedFileName = "Galaxy Tab.absync",
                notWrapped = listOf("Old Laptop (an older version)"), lockedReason = if (locked) "This is locked while a focus session is running." else null,
                step = step, backup = RemoveBackupChoice.Drive, problem = "Google Drive: no connection.", canSaveFileInstead = true,
                doneTitle = "Galaxy Tab was removed",
                summary = listOf(
                    "Galaxy Tab can no longer read new data from your devices. To use it again it has to be linked again.",
                    "Its data is kept on every device. Devices & sync shows it as \"Galaxy Tab (removed)\".",
                    "Backup saved: Google Drive, Backups/Galaxy Tab (3f9a)/2026-11-02.absync",
                ),
            )
            Box(Modifier.fillMaxSize().background(Ab.colors.bg)) { RemoveDeviceSheet(state, flow) }
        }
        "restore" -> {
            val flow = remember { RestoreBackupFlow(app, app.sync, app.backupOpener, com.accountabilibuddy.companion.sync.DriveBackups(app.driveSignIn), scope) }
            val backups = listOf(
                DriveBackupInfo("1", "Galaxy Tab", "2026-11-01.absync", Instant.now().minus(Duration.ofDays(1)), 4096),
                DriveBackupInfo("2", "Old Laptop", "2026-10-20.absync", Instant.now().minus(Duration.ofDays(13)), 4096),
            )
            val state = when (intent.getStringExtra("mode")) {
                "empty" -> RestoreState(loading = false)
                "problem" -> RestoreState(loading = false, problem = "Google Drive isn't set up on this phone.")
                else -> RestoreState(loading = false, backups = backups, message = "Restored Old Laptop: its history is back and shows as \"Old Laptop (removed)\" in Settings › Devices & sync.")
            }
            Box(Modifier.fillMaxSize().background(Ab.colors.bg)) { RestoreBackupSheet(state, flow) }
        }
        else -> DevicesPage(intent)
    }
}

@Composable
private fun DevicesPage(intent: Intent) {
    val now = remember { Instant.now() }
    val pc = remember { DemoPcFile.build(now.minus(Duration.ofMinutes(1)), withSession = false, methods = listOf("drive")) }
    val tablet = remember {
        pc.copy(device = SyncDeviceCard(UUID.randomUUID(), "Galaxy Tab", SyncDeviceCard.KIND_PHONE, "3.3.9", now.minus(Duration.ofHours(30)), listOf("drive"), false, emptyMap()))
    }
    val watchCard = remember {
        pc.copy(device = SyncDeviceCard(UUID.randomUUID(), "Pixel Watch 3", SyncDeviceCard.KIND_WATCH, "3.4.1", now.minus(Duration.ofMinutes(1)), listOf("drive"), false, emptyMap()))
    }
    fun received(file: com.accountabilibuddy.shared.sync.SyncFile) = ReceivedDevice(file, file.device.writtenAt, SyncDeviceCard.METHOD_DRIVE, ByteArray(0))
    val noWatch = intent.getBooleanExtra("nowatch", false)
    val relayOn = intent.getStringExtra("relay") == "on"
    val pendingOff = com.accountabilibuddy.companion.sync.SyncMethod.fromKey(intent.getStringExtra("pending"))?.let {
        com.accountabilibuddy.companion.sync.PendingSyncOff(it, if (intent.getBooleanExtra("due", false)) now.minus(Duration.ofMinutes(1)) else now.plus(Duration.ofMinutes(7)))
    }
    val settings = PhoneSyncSettings(driveOn = pendingOff == null || pendingOff.method == com.accountabilibuddy.companion.sync.SyncMethod.Drive, relayOn = relayOn || pendingOff?.method == com.accountabilibuddy.companion.sync.SyncMethod.Relay,
        relayUrl = "https://192.168.1.20:8743".takeIf { relayOn || pendingOff?.method == com.accountabilibuddy.companion.sync.SyncMethod.Relay })
    val devices = DevicesState(
        deviceName = "Nothing Phone 3", joined = true, settings = settings,
        received = listOf(received(pc), received(tablet), received(watchCard)),
        drive = com.accountabilibuddy.companion.sync.FolderStatus(lastSyncAt = now.minus(Duration.ofMinutes(1))),
        appVersion = "3.4.2",
        syncOffPending = pendingOff,
        removalLocked = "This is locked while a focus session is running.".takeIf { intent.getBooleanExtra("locked", false) },
    )
    val watch = if (noWatch) WatchSectionState() else WatchSectionState(WatchLinkSettings("node", "Pixel Watch 3"), listOf(WatchNode("node", "Pixel Watch 3", true)))
    val page = DevicesPageInputs(
        devices, DevicesActions(), watch, WatchSectionActions(),
        pcChoices = emptyList(), chosenPcId = null, onChoosePc = {}, syncNow = rememberSyncNow(),
    )
    Box(Modifier.fillMaxSize().background(Ab.colors.bg).safeDrawingPadding()) {
        SettingsPageFrame(SettingsCategory.Devices, onBack = {}) { DevicesScreen(page, intent.getStringExtra("anchor")) }
    }
}
