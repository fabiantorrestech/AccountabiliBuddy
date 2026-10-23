package com.accountabilibuddy.companion

import android.app.Application
import android.os.Build
import android.provider.Settings
import com.accountabilibuddy.companion.data.CompanionStore
import com.accountabilibuddy.companion.data.ImportException
import com.accountabilibuddy.companion.data.Importer
import com.accountabilibuddy.companion.link.DayPlanSync
import com.accountabilibuddy.companion.link.LinkClient
import com.accountabilibuddy.companion.link.LinkDiscovery
import com.accountabilibuddy.companion.link.LinkStore
import com.accountabilibuddy.companion.notify.Refresher
import com.accountabilibuddy.companion.sync.DriveFolderSource
import com.accountabilibuddy.companion.sync.AlarmHistoryStore
import com.accountabilibuddy.companion.sync.NextAlarm
import com.accountabilibuddy.companion.sync.WakeAlarm
import com.accountabilibuddy.companion.sync.PhoneSyncSettingsStore
import com.accountabilibuddy.companion.sync.ReceivedDevicesStore
import com.accountabilibuddy.companion.sync.SyncCoordinator
import com.accountabilibuddy.companion.sync.SyncIdentityStore
import com.accountabilibuddy.companion.watch.WatchBridge
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.companion.watch.WatchLinkStore
import com.accountabilibuddy.shared.FocusDayJson
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import androidx.glance.appwidget.updateAll
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.merge
import kotlinx.serialization.json.JsonObject
import java.io.File

/**
 * The app's single store, the link to the PC (ADR 14), the link to the watch (ADR 21), and the refresh loop for the
 * notification, widget and bedtime reminder.
 */
class CompanionApp : Application() {
    lateinit var store: CompanionStore
        private set

    lateinit var link: LinkClient
        private set

    /** This phone's name as the PC shows it (e.g. "Pixel 7"). */
    lateinit var deviceName: String
        private set

    /** Sends day plan edits made on the phone to the PC (ADR 19). */
    lateinit var planSync: DayPlanSync
        private set

    /** Sends the linked Pixel Watch its snapshot (ADR 21). */
    lateinit var watchBridge: WatchBridge
        private set

    /** Device sync (ADR 22): the group, the other devices' parts, and the files the phone carries. */
    lateinit var sync: SyncCoordinator
        private set

    /** The sync folder another app keeps in step with Google Drive (ADR 22, way B). */
    lateinit var folderSource: DriveFolderSource
        private set

    /** Google Drive (ADR 26): the app's own Drive folder, the one the PCs use. */
    lateinit var driveSource: com.accountabilibuddy.companion.sync.DriveSyncSource
        private set
    val driveSignIn by lazy { com.accountabilibuddy.companion.sync.DriveSignIn(this) }

    /** The phone's master switch and car pause. */
    val pause by lazy { com.accountabilibuddy.companion.pause.PauseStore(this) }

    /** The noise player (sounds from a folder the user picks), as on the PC. */
    lateinit var noise: com.accountabilibuddy.companion.noise.NoisePlayer
        private set

    /** True while a screen of the app is showing (set by the screens' view model). */
    @Volatile
    var appOnScreen: Boolean = false

    // Lives as long as the app; the link reads from the PC here.
    private val appScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    override fun onCreate() {
        super.onCreate()
        store = CompanionStore(File(filesDir, "companion"))
        deviceName = readDeviceName()
        val linkStore = LinkStore(File(filesDir, "companion/link.json"))
        sync = SyncCoordinator(
            SyncIdentityStore(File(filesDir, "companion/sync.json")),
            ReceivedDevicesStore(File(filesDir, "companion/sync-received.json")),
            deviceName,
            appVersion(),
            PhoneSyncSettingsStore(File(filesDir, "companion/sync-settings.json")),
            pcPaired = { linkStore.pc.value != null },
        )
        link = LinkClient(
            linkStore, deviceName, deviceKind(), appScope, LinkDiscovery(this), ::applyPcSettings,
            // Without the PC's plan version (a phone-only update, a cleared import) ask for everything again.
            needsFullSettings = { store.state.value.hasImport && store.state.value.pcPlanVersion == null },
            applyFocusDay = { content -> FocusDayJson.read(content)?.let(store::saveFocusDay) },
            onForget = store::clearFocusDay,
            onSyncFile = { sealed -> sync.acceptSealed(sealed, SyncDeviceCard.METHOD_LINK) },
            // Before each sync file the phone writes for the PC, it checks its watch link is still valid (Task 18).
            syncFilesForPc = { pcName ->
                watchBridge.refreshKnownWatches()
                sync.filesForPc(pcName)
            },
        )
        // A file that came by QR code, Drive or a share goes on to the PC at once when connected.
        sync.onCarryChanged = { sendSyncFilesToPc() }
        planSync = DayPlanSync(store, link, appScope).also { it.start() }
        watchBridge = WatchBridge(this, store, link, WatchLinkStore(File(filesDir, "companion/watch.json")), appScope, { appOnScreen }, sync.received)
            .also { it.start() }
        // The phone's card names its linked watch (Task 18); linking or unlinking it tells the PC at once.
        sync.companions = { watchBridge.companions() }
        sync.nextAlarm = { wakeAlarm }
        // Pickups during the other devices' sessions (ADR 26): unlock times from Usage access, when allowed.
        // Unlocks while the phone was paused or in the car aren't pickups.
        sync.unlocksSince = { from ->
            val state = pause.advance()
            com.accountabilibuddy.companion.usage.PhoneUsageReader(this).unlocks(from, java.time.Instant.now())
                ?.filterNot { com.accountabilibuddy.companion.pause.PauseRules.inQuiet(state, it) }
        }
        watchBridge.onLinkChanged = { sendSyncFilesToPc() }
        folderSource = DriveFolderSource(this, sync)
        driveSource = com.accountabilibuddy.companion.sync.DriveSyncSource(sync, driveSignIn)
        // Drive (ADR 26): every 30 s while a session runs or the timeline is pinned, else 15 min.
        appScope.launch { driveSource.loop { driveBusy() } }
        noise = com.accountabilibuddy.companion.noise.NoisePlayer(this, com.accountabilibuddy.companion.noise.NoiseSettingsStore(this), appScope)
        // The widget's play/pause follows the player.
        noise.onChanged = {
            appScope.launch { runCatching { com.accountabilibuddy.companion.widget.TimelineWidget().updateAll(this@CompanionApp) } }
        }
        refreshWakeAlarm()
        // The background sync follows the folder switch; a change of folder or interval syncs at once.
        appScope.launch {
            sync.settings.collect {
                refreshWakeAlarm()
                folderSource.schedule()
                folderSource.syncNow()
                if (it.driveOn) driveSource.syncNow() else driveSource.forget()
            }
        }
        // The header follows the PC's session and the devices' focus: redraw (at most every 2 s) when they change.
        appScope.launch {
            merge(link.status, link.lastState, sync.received, store.state.map { it.focusDay }.distinctUntilChanged())
                .debounce(2_000)
                .collect { Refresher.refresh(this@CompanionApp) }
        }
        Refresher.start(this)
    }

    /** Android's next alarm changed (ADR 23): the wake time, the header, and this phone's file for the PCs follow. */
    fun onNextAlarmChanged() {
        refreshWakeAlarm()
        sendSyncFilesToPc()
        appScope.launch {
            folderSource.syncNow()
            driveSource.syncNow()
            Refresher.refresh(this@CompanionApp)
        }
    }

    // A session runs somewhere (the linked PC, or a device's file from the last 12 hours) or the timeline is pinned.
    private fun driveBusy(): Boolean {
        if (com.accountabilibuddy.companion.notify.PinnedTimelineService.isRunning || link.lastState.value?.state?.isActive == true) {
            return true
        }

        val now = java.time.Instant.now()
        return sync.received.value.any { device ->
            java.time.Duration.between(device.file.device.writtenAt, now) <= java.time.Duration.ofHours(12) &&
                device.file.sessions.any { it.end == null }
        }
    }

    /** The alarms seen as next (for learning the wake-up alarm app). */
    val alarmHistory by lazy { AlarmHistoryStore(java.io.File(filesDir, "companion/alarms.json")) }

    /** The wake-up alarm now ([WakeAlarm]): the pinned or learned app's next alarm; null when there's none. */
    @Volatile
    var wakeAlarm: java.time.Instant? = null
        private set

    /** The app learned as the wake-up alarm, for the Devices tab; null until two mornings show it. */
    @Volatile
    var learnedAlarmApp: String? = null
        private set

    /**
     * The phone's wake time follows its wake-up alarm while "Next alarm as wake time" is on (ADR 23): Android's next
     * alarm is remembered with the app that set it, the wake-up app is pinned or learned from mornings in the sleep
     * plan, and calendar apps never count.
     */
    fun refreshWakeAlarm() {
        val zone = java.time.ZoneId.systemDefault()
        val now = java.time.Instant.now()
        val next = NextAlarm.read(this)
        val history = next?.let { WakeAlarm.remember(alarmHistory.history, it) } ?: alarmHistory.history
        if (history != alarmHistory.history) {
            alarmHistory.save(history)
        }

        val state = store.state.value
        val plan = state.sleep.copy(alarm = null).toSchedule(state.document.dayPlan)
        learnedAlarmApp = WakeAlarm.learned(history) { at ->
            WakeAlarm.inMorningWindow(java.time.LocalDateTime.ofInstant(at, zone), plan::night)
        }
        val settings = sync.settings.value
        wakeAlarm = WakeAlarm.choose(next, history, settings.alarmApp, learnedAlarmApp, now)
        store.setPhoneAlarm(wakeAlarm?.takeIf { settings.alarmOn }?.let { java.time.LocalDateTime.ofInstant(it, zone) })
    }

    /** Hands the PC this phone's file and what it carries now, when a PC is paired (and connected), after checking the watch link. */
    fun sendSyncFilesToPc() {
        val pc = link.pairedPc.value ?: return
        appScope.launch {
            watchBridge.refreshKnownWatches()
            link.sendSyncFiles(sync.filesForPc(pc.pcName))
        }
    }

    /** The linked PC's profiles, sleep, day bar and events (L2): kept like an import, then the card and widget redraw. */
    private fun applyPcSettings(content: JsonObject, pcName: String): Boolean {
        val data = try {
            Importer.fromLink(content, pcName)
        } catch (e: ImportException) {
            return false
        }

        store.import(data)
        appScope.launch { Refresher.refresh(this@CompanionApp) }
        return true
    }

    /** E.g. "Fabian's Pixel 7" (the name set in Android's settings), else the model. */
    private fun readDeviceName(): String =
        Settings.Global.getString(contentResolver, Settings.Global.DEVICE_NAME)?.takeIf { it.isNotBlank() } ?: Build.MODEL

    private fun appVersion(): String =
        runCatching { packageManager.getPackageInfo(packageName, 0).versionName }.getOrNull() ?: "?"

    private fun deviceKind(): String = if (resources.configuration.smallestScreenWidthDp >= 600) "tablet" else "phone"
}
