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

    /** Today's flexible lunch, the same on every device (schema v8). */
    val lunch by lazy { com.accountabilibuddy.companion.lunch.LunchStore(this) }

    /** Bedtime reminders between devices (ADR 38). */
    lateinit var reminderRelay: com.accountabilibuddy.companion.notify.ReminderRelay

    /** Hands the PC this phone's file soon (a reminder dismissed here). */
    fun sendSyncFilesSoon() {
        appScope.launch { sendSyncFilesToPc() }
    }

    /** The noise player on the linked watch (ADR 36). */
    lateinit var noiseWatch: com.accountabilibuddy.companion.watch.NoiseWatchSync

    /** The noise player (sounds from a folder the user picks), as on the PC. */
    lateinit var noise: com.accountabilibuddy.companion.noise.NoisePlayer
        private set

    /** The essential apps (allowed, the essentials screen), folders, and the LockMyPhone-Extended hardcore profile. */
    val essentials by lazy { com.accountabilibuddy.companion.essentials.EssentialsStore(this) }

    /** The sleep settings both ways with the PCs (ADR 35). */
    val sleepSync by lazy { com.accountabilibuddy.companion.sync.SleepSettingsSync(this, store) }

    /** The smart bedtime (ADR 34): learns tonight's bedtime from measured sleep. */
    val smartBedtime by lazy { com.accountabilibuddy.companion.engine.SmartBedtimeService(this) }

    /** Auto-backup of this phone's settings to a chosen folder. */
    val autoBackup by lazy { com.accountabilibuddy.companion.backup.AutoBackup(this, appVersion()) }

    /** This app's version, e.g. "0.1.99". */
    fun versionName(): String = appVersion()

    /** "I need my phone for a bit". */
    val emergency by lazy { com.accountabilibuddy.companion.nudge.EmergencyStore(this) }

    /** What the user did here for a PC (ADR 26), until that PC acknowledges it. */
    val actions by lazy { com.accountabilibuddy.companion.sync.ActionStore(this) }

    /** "Play when headphones connect" (Settings → Noise player). */
    val headphones by lazy { com.accountabilibuddy.companion.noise.HeadphoneStore(this) }

    /** True while a screen of the app is showing (set by the screens' view model). */
    @Volatile
    var appOnScreen: Boolean = false

    /** True while the essentials screen is showing (no work card over it). */
    @Volatile
    var essentialsOnScreen: Boolean = false

    // Lives as long as the app; the link reads from the PC here.
    private val appScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    override fun onCreate() {
        super.onCreate()
        store = CompanionStore(File(filesDir, "companion"))
        // The migration assistant: the version that ran before this one.
        com.accountabilibuddy.companion.setup.SetupFeatures.recordLaunch(
            this, appVersion(), getSharedPreferences("ui", MODE_PRIVATE).getBoolean("setupShown", false))
        // A setting saved here or in a backed-up preference file: auto-backup a minute later.
        store.onSettingsSaved = { autoBackup.soon() }
        autoBackup.start()
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
        sync.smartBedtime = { smartBedtime.forSyncFile() }
        sync.sleepDocument = { sleepSync.document() }
        // The smart bedtime's switches used to be kept apart (ADR 34): handed to the shared sleep settings once.
        smartBedtime.store.takeOldSettings()?.let { old ->
            if (old.workdays || old.offDays) {
                sleepSync.saveEdit(store.state.value.sleep.copy(smartWorkdays = old.workdays, smartOffDays = old.offDays, minHours = old.minHours, maxHours = old.maxHours))
            }
        }
        // A newer PC's sleep settings arriving in any sync file are taken here (ADR 35).
        appScope.launch {
            sync.received.collect { devices -> if (sleepSync.applyFrom(devices)) refreshSoon() }
        }
        // Lunch is the user's: this phone's goes out in its file; one from another device is taken here.
        sync.lunch = { lunch.today() }
        sync.actions = { actions.pending.value }
        // A tick or a parked thought goes to the PC at once (link, Drive), and shows ticked here meanwhile.
        actions.onChanged = {
            sendSyncFilesToPc()
            appScope.launch {
                driveSource.syncNow()
                Refresher.refresh(this@CompanionApp)
            }
        }
        lunch.onChanged = {
            sendSyncFilesToPc()
            appScope.launch {
                driveSource.syncNow()
                Refresher.refresh(this@CompanionApp)
            }
        }
        appScope.launch {
            sync.received.collect { devices ->
                devices.mapNotNull { it.file.lunch }.forEach { lunch.receive(it) }
                actions.prune(devices)
                // The newest PC file's calendar events, when newer than what the phone has (ADR 26).
                devices.filter { it.file.device.kind == com.accountabilibuddy.shared.sync.SyncDeviceCard.KIND_PC && it.file.events != null }
                    .maxByOrNull { it.file.device.writtenAt }
                    ?.let { store.eventsFromFile(it.file.events!!, it.file.device.writtenAt) }
            }
        }
        // Pickups during the other devices' sessions (ADR 26): unlock times from Usage access, when allowed.
        // Unlocks while the phone was paused or in the car aren't pickups.
        sync.unlocksSince = { from ->
            val state = pause.advance()
            val reader = com.accountabilibuddy.companion.usage.PhoneUsageReader(this)
            val uses = emergency.all()
            val allowed = essentials.essentials.value
            reader.unlocks(from, java.time.Instant.now())
                ?.filterNot { com.accountabilibuddy.companion.pause.PauseRules.inQuiet(state, it) }
                // Emergency time and unlocks into an always-allowed app (a call, a text) are shown apart, not as pickups.
                ?.filterNot { unlock -> uses.any { !unlock.isBefore(it.start) && unlock.isBefore(it.end) } }
                ?.filterNot { unlock -> reader.appAfter(unlock)?.let { (app, screen) -> allowed.isAllowed(app, screen) } == true }
        }
        sync.emergencyWindows = { emergency.all().map { it.start to minOf(it.end, java.time.Instant.now()) } }
        watchBridge.onLinkChanged = { sendSyncFilesToPc() }
        folderSource = DriveFolderSource(this, sync)
        com.accountabilibuddy.companion.ui.UiPrefs.load(this)
        driveSource = com.accountabilibuddy.companion.sync.DriveSyncSource(sync, driveSignIn)
        // Drive (ADR 26): every 30 s while a session runs or the timeline is pinned, else 15 min.
        appScope.launch { driveSource.loop { driveBusy() } }
        noise = com.accountabilibuddy.companion.noise.NoisePlayer(this, com.accountabilibuddy.companion.noise.NoiseSettingsStore(this), appScope)
        com.accountabilibuddy.companion.noise.HeadphoneWatcher(this, headphones, noise).start()
        noiseWatch = com.accountabilibuddy.companion.watch.NoiseWatchSync(this, noise, watchBridge.linkSettings, appScope).also { it.start() }
        reminderRelay = com.accountabilibuddy.companion.notify.ReminderRelay(this, watchBridge.linkSettings, appScope)
        sync.reminderActions = { reminderRelay.actions.value }
        sync.blockers = { com.accountabilibuddy.companion.blocks.BlockGuard.lastBlockers(this) }
        // A PC's dismiss or snooze stops the reminder here (ADR 38).
        appScope.launch {
            sync.received.collect { devices -> devices.flatMap { it.file.reminderActions.orEmpty() }.forEach(reminderRelay::received) }
        }
        // The widget's play/pause follows the player.
        noise.onChanged = {
            appScope.launch { runCatching { com.accountabilibuddy.companion.widget.TimelineWidget().updateAll(this@CompanionApp) } }
            // The timeline card's play/pause follows too.
            appScope.launch { Refresher.refresh(this@CompanionApp) }
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
    /** Redraws the notification, widget and nudges soon (after the full-screen card closes, a setting changes). */
    fun refreshSoon() {
        appScope.launch { Refresher.refresh(this@CompanionApp) }
    }

    /**
     * The header's refresh: connects to the PC (which sends its state and settings), swaps sync files with it, reads
     * the sync folder and Drive, then redraws. [onDone] gets what to say.
     */
    fun refreshNow(onDone: (String) -> Unit) {
        appScope.launch {
            link.connect()
            sendSyncFilesToPc()
            val problems = listOfNotNull(
                runCatching { folderSource.syncNow() }.exceptionOrNull()?.let { "sync folder: ${it.message}" },
                runCatching { driveSource.syncNow() }.exceptionOrNull()?.let { "Drive: ${it.message}" },
            )
            Refresher.refresh(this@CompanionApp)
            val at = java.time.LocalTime.now().withSecond(0).withNano(0)
            val text = if (problems.isEmpty()) "Up to date · refreshed $at" else "Refreshed $at, but ${problems.joinToString("; ")}"
            kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.Main) { onDone(text) }
        }
    }

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

    internal fun appVersion(): String =
        runCatching { packageManager.getPackageInfo(packageName, 0).versionName }.getOrNull() ?: "?"

    private fun deviceKind(): String = if (resources.configuration.smallestScreenWidthDp >= 600) "tablet" else "phone"
}
