package com.accountabilibuddy.companion

import android.app.Application
import android.os.Build
import android.provider.Settings
import com.accountabilibuddy.companion.data.CompanionStore
import com.accountabilibuddy.companion.data.ImportException
import com.accountabilibuddy.companion.data.Importer
import com.accountabilibuddy.companion.notify.Refresher
import com.accountabilibuddy.companion.sync.DriveFolderSource
import com.accountabilibuddy.companion.sync.AlarmHistoryStore
import com.accountabilibuddy.companion.sync.NextAlarm
import com.accountabilibuddy.companion.sync.WakeAlarm
import com.accountabilibuddy.companion.sync.WakeEstimate
import com.accountabilibuddy.companion.sync.AlarmSighting
import com.accountabilibuddy.companion.sync.PhoneSyncSettingsStore
import com.accountabilibuddy.companion.sync.ReceivedDevicesStore
import com.accountabilibuddy.companion.sync.SyncCoordinator
import com.accountabilibuddy.companion.sync.SyncIdentityStore
import com.accountabilibuddy.companion.watch.WatchBridge
import com.accountabilibuddy.companion.watch.WatchLinkStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import androidx.glance.appwidget.updateAll
import com.accountabilibuddy.companion.sync.DayPlanFileSync
import com.accountabilibuddy.companion.sync.PcFileRemote
import com.accountabilibuddy.companion.sync.PcNoiseRemote
import com.accountabilibuddy.shared.sync.SyncAction
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.debounce
import kotlinx.serialization.json.JsonObject
import java.io.File

// How often a waiting PC command is checked against its 10 minutes (PcFileRemote.GIVE_UP_AFTER) when no file arrives.
private const val EXPIRE_TICK_MS = 60_000L

// Unlocking the phone syncs at most this often ([CompanionApp.syncOnUnlock]).
private val UNLOCK_SYNC_GAP: java.time.Duration = java.time.Duration.ofMinutes(1)

// A burst of files from the relay (a busy PC writes every few seconds) is carried on to Drive once.
private const val DRIVE_CARRY_DELAY_MS = 5_000L

/**
 * The app's single store, the sync with the other devices (ADR 22, 26, 42), the link to the watch (ADR 21), and the
 * refresh loop for the notification, widget and bedtime reminder.
 */
class CompanionApp : Application() {
    lateinit var store: CompanionStore
        private set

    /** This phone's name as the PC shows it (e.g. "Pixel 7"). */
    lateinit var deviceName: String
        private set

    /** Starts and ends the PC's sessions through the sync files (ADR 42). */
    lateinit var pcRemote: PcFileRemote
        private set

    /** Sends day plan edits through the sync files (ADR 42). */
    lateinit var planFileSync: DayPlanFileSync
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

    /** The user's own sealed relay (ADR 46): the same files as Drive, pushed in about a second while the phone is busy. */
    lateinit var relaySource: com.accountabilibuddy.companion.sync.RelaySyncSource
        private set

    /** The phone's master switch and car pause. */
    val pause by lazy { com.accountabilibuddy.companion.pause.PauseStore(this) }

    /** Today's flexible lunch, the same on every device (schema v8). */
    val lunch by lazy { com.accountabilibuddy.companion.lunch.LunchStore(this) }

    /** The phone's own timers and alarms (V1, never leave the phone). */
    val timers by lazy { com.accountabilibuddy.companion.timers.TimersStore(this) }

    /** A test session to try the phone's settings (Today → More options); never counted, synced or sent to the watch. */
    val testSession by lazy { com.accountabilibuddy.companion.session.TestSession(this).also { it.onChanged = ::refreshSoon } }

    /** Bedtime reminders between devices (ADR 38). */
    lateinit var reminderRelay: com.accountabilibuddy.companion.notify.ReminderRelay

    /** Syncs with Google Drive and the relay in the background, so the PCs get this phone's file and what it carries soon (a reminder dismissed here). */
    fun sendSyncFilesSoon() {
        appScope.launch { runCatching { driveSource.syncNow() } }
        relaySoon()
    }

    private var driveCarry: kotlinx.coroutines.Job? = null

    /** Syncs Drive once, a few seconds from now, for a burst of files that came by the relay. */
    @Synchronized
    private fun driveCarrySoon() {
        if (driveCarry?.isActive == true) {
            return
        }

        driveCarry = appScope.launch {
            kotlinx.coroutines.delay(DRIVE_CARRY_DELAY_MS)
            runCatching { driveSource.syncNow() }
        }
    }

    /** Sends the relay this phone's file now (a phone action, ADR 46); nothing happens while the relay is off. */
    private fun relaySoon() {
        appScope.launch { runCatching { relaySource.syncNow() } }
    }

    /** The noise player on the linked watch (ADR 36). */
    lateinit var noiseWatch: com.accountabilibuddy.companion.watch.NoiseWatchSync

    /** The noise player (sounds from a folder the user picks), as on the PC. */
    lateinit var noise: com.accountabilibuddy.companion.noise.NoisePlayer
        private set

    /** The essential apps (allowed, the essentials screen), folders, and the LockMyPhone-Extended hardcore profile. */
    val essentials by lazy { com.accountabilibuddy.companion.essentials.EssentialsStore(this) }
    /** The night's essentials (Settings → Sleep): from bedtime until the wake time. */
    val bedtimeEssentials by lazy { com.accountabilibuddy.companion.essentials.EssentialsStore(this, com.accountabilibuddy.companion.essentials.EssentialsKind.Bedtime) }

    /** The essentials list of [kind]. */
    fun essentialsOf(kind: com.accountabilibuddy.companion.essentials.EssentialsKind) =
        if (kind == com.accountabilibuddy.companion.essentials.EssentialsKind.Bedtime) bedtimeEssentials else essentials

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

    /** The on/off switch for the placement guard (Settings; off waits 10 minutes, never in a session). */
    val placementGuard by lazy { com.accountabilibuddy.companion.placement.PlacementGuard(this) }

    /** The "Phone down" screen's pocket penalty switches and its log (phase 11b; never synced). */
    val phoneDown by lazy { com.accountabilibuddy.companion.placement.PhoneDownStore(this) }

    /** Where the phone is during a PC session (phase 11b): the state and counts the sync file carries. */
    val placementStore by lazy { com.accountabilibuddy.companion.placement.PlacementStore(this) }

    /** "Need it for work" uses during a session (phase 11b); the reasons stay on the phone. */
    val workUse by lazy { com.accountabilibuddy.companion.placement.WorkUseStore(this) }

    /** What the user did here for a PC (ADR 26), until that PC acknowledges it. */
    val actions by lazy { com.accountabilibuddy.companion.sync.ActionStore(this) }

    /** The shared next-session tasks and come-backs (ADR 48): items with edit times, merged with the other devices' lists. */
    val sharedLists by lazy { com.accountabilibuddy.companion.sync.SharedListsStore(this) }

    /** Why the user added a 4th task to the next session's list (ADR 48); stays on this phone. */
    val overLimit by lazy { com.accountabilibuddy.companion.sync.OverLimitStore(this) }

    /** Sends add/edit/reorder of a running PC session's tasks as sync actions the PC applies under its own rules (ADR 48). */
    val runningTaskEdits: com.accountabilibuddy.companion.sync.RunningTaskEdits by lazy {
        com.accountabilibuddy.companion.sync.PcFileRunningTaskEdits(pcRemote)
    }

    /** "Play when headphones connect" (Settings → Noise player). */
    val headphones by lazy { com.accountabilibuddy.companion.noise.HeadphoneStore(this) }

    /** True while a screen of the app is showing (set by the screens' view model). */
    @Volatile
    var appOnScreen: Boolean = false

    /** True while the essentials screen is showing (no work card over it). */
    @Volatile
    var essentialsOnScreen: Boolean = false

    /** True while the "Phone down" screen is in front (it is not an app in use, and no other full-screen card opens over it). */
    @Volatile
    var phoneDownOnScreen: Boolean = false

    // Lives as long as the app; the sync work runs here.
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
        // ADR 45: the Wi-Fi link's stored pairing and focus day are deleted once.
        com.accountabilibuddy.companion.sync.LinkLeftovers.deleteOnce(this)
        sync = SyncCoordinator(
            SyncIdentityStore(File(filesDir, "companion/sync.json")),
            ReceivedDevicesStore(File(filesDir, "companion/sync-received.json")),
            deviceName,
            appVersion(),
            PhoneSyncSettingsStore(File(filesDir, "companion/sync-settings.json")),
        )
        // A new file that came by QR code, a folder or a share goes on to the PCs through Drive and the relay soon; one
        // that came by Drive also goes to the relay, and one that came by the relay to Drive (after a pause, since a
        // busy PC's file arrives every few seconds), never back where it came from.
        sync.onCarryChanged = { method ->
            when (method) {
                com.accountabilibuddy.shared.sync.SyncDeviceCard.METHOD_DRIVE -> relaySource.syncSoon()
                com.accountabilibuddy.shared.sync.SyncDeviceCard.METHOD_RELAY -> driveCarrySoon()
                else -> {
                    appScope.launch { runCatching { driveSource.syncNow() } }
                    relaySource.syncSoon()
                }
            }
        }
        // ADR 42: starting and ending the PC's sessions and sending plan edits go through the sync files.
        pcRemote = PcFileRemote(this, actions, sync.received)
        planFileSync = DayPlanFileSync(store, actions, pcRemote, sync.received, appScope).also { it.start() }
        // ADR 42, 36: the watch's Noise page controls a PC's noise player through the same files.
        val pcNoise = PcNoiseRemote(actions, sync.received, appScope)
        actions.onAnswered = { action, result, pc ->
            pcRemote.answered(action, result, pc)
            planFileSync.answered(action, result, pc)
            pcNoise.answered(action, result, pc)
        }
        // ADR 40: the PC's settings (profiles, day plan, sleep, day bar, events) from its sync file.
        appScope.launch { sync.received.collect { devices -> applyPcFileSettings(devices.map { it.file }) } }
        watchBridge = WatchBridge(
            this, store, WatchLinkStore(File(filesDir, "companion/watch.json")), appScope, sync.received,
            pickedPc = { pcRemote.chosenPcId.value },
            // The watch's Lunch page: today's lunch and the length setting.
            lunch = { lunch.forWatch(store.state.value.document.dayPlan) },
            lunchChanges = kotlinx.coroutines.flow.combine(lunch.lunch, lunch.lengthMinutes) { taken, length -> taken to length },
            // ADR 42: the watch's "/hello" reads the PCs' files from Drive before it answers.
            syncNow = {
                driveSource.syncNow()
                relaySource.syncNow()
            },
        )
            .also { it.start() }
        // The phone's card names its linked watch (Task 18); linking or unlinking it tells the PC at once.
        sync.companions = { watchBridge.companions() }
        sync.nextAlarm = { wakeAlarm }
        sync.smartBedtime = { smartBedtime.forSyncFile() }
        sync.wakeEstimate = { wakeEstimateForSync() }
        sync.sleepDocument = { sleepSync.document() }
        // A held sleep-mode off (10 minutes) applies when due, also after a restart.
        sleepSync.onPendingOff = ::watchSleepOff
        watchSleepOff()
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
        // A tick or a parked thought goes to the PC at once (Drive), and shows ticked here meanwhile.
        actions.onChanged = {
            relaySoon()
            appScope.launch {
                driveSource.syncNow()
                Refresher.refresh(this@CompanionApp)
            }
        }
        // ADR 48: the next session's tasks and the come-backs are shared lists; this phone's go out in its file, other devices' merge in.
        sync.nextTasks = { sharedLists.nextTasksForSync() }
        sync.parked = { sharedLists.parkedForSync() }
        sync.later = { sharedLists.laterForSync() }
        sync.onSharedLists = { nextTasks, parked, later -> sharedLists.receive(nextTasks, parked, later) }
        sharedLists.onChanged = {
            relaySoon()
            appScope.launch {
                driveSource.syncNow()
                Refresher.refresh(this@CompanionApp)
            }
        }
        lunch.onChanged = {
            relaySoon()
            appScope.launch {
                // The sync folder too: a lunch started here reaches the PCs without waiting for the next folder sync.
                runCatching { folderSource.syncNow() }
                driveSource.syncNow()
                Refresher.refresh(this@CompanionApp)
            }
        }
        appScope.launch {
            sync.received.collect { devices ->
                devices.mapNotNull { it.file.lunch }.forEach { lunch.receive(it) }
                runCatching { com.accountabilibuddy.companion.essentials.LunchLockLift.check(this@CompanionApp) }
                actions.prune(devices)
                pcRemote.expire()
                pcNoise.expire()
                // The newest PC file's calendar events, when newer than what the phone has (ADR 26).
                devices.filter { it.file.device.kind == com.accountabilibuddy.shared.sync.SyncDeviceCard.KIND_PC && it.file.events != null }
                    .maxByOrNull { it.file.device.writtenAt }
                    ?.let { store.eventsFromFile(it.file.events!!, it.file.device.writtenAt) }
            }
        }
        // A PC that's off sends no file, so a waiting start, end early or noise command is also given up on a minute tick.
        appScope.launch {
            actions.pending
                .map { pending -> pending.any { it.kind in SyncAction.CONTROL_KINDS || it.kind in SyncAction.TASK_KINDS || it.kind == SyncAction.NOISE } }
                .distinctUntilChanged()
                .collectLatest { waiting ->
                    while (waiting) {
                        delay(EXPIRE_TICK_MS)
                        pcRemote.expire()
                        pcNoise.expire()
                    }
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
                // An unlock that showed the "Phone down" screen is a pickup whatever came next (Essentials / quick reply).
                ?.filterNot { unlock -> !phoneDown.shownFor(unlock) && reader.appAfter(unlock)?.let { (app, screen) -> allowed.isAllowed(app, screen) } == true }
        }
        // Phase 11b: where the phone is during a PC session (label, times and counts only).
        sync.inFocusSession = { placementStore.load()?.active == true }
        sync.placement = {
            // Guard off: no placement in the file, so the PC sees unknown and never nags.
            val now = java.time.Instant.now()
            if (placementGuard.isOn(now)) placementStore.forSyncFile(now, workUse) else null
        }
        sync.emergencyWindows = { emergency.all().map { it.start to minOf(it.end, java.time.Instant.now()) } }
        // Linking or unlinking the watch tells the PCs through Drive at once (Task 18).
        watchBridge.onLinkChanged = { sendSyncFilesSoon() }
        folderSource = DriveFolderSource(this, sync)
        com.accountabilibuddy.companion.ui.UiPrefs.load(this)
        driveSource = com.accountabilibuddy.companion.sync.DriveSyncSource(sync, driveSignIn, beforeSync = { watchBridge.refreshKnownWatches() })
        // The relay source comes first: a file the Drive loop accepts at start-up calls relaySource.syncSoon().
        relaySource = com.accountabilibuddy.companion.sync.RelaySyncSource(sync, appScope, beforeSync = { watchBridge.refreshKnownWatches() })
        // Drive (ADR 26): every 30 s while a session runs or the timeline is pinned, else 15 min.
        appScope.launch { driveSource.loop { driveBusy() } }
        // The relay (ADR 46): the same pace, plus a push stream open while a session runs or the timeline is pinned.
        appScope.launch { relaySource.loop { driveBusy() } }
        noise = com.accountabilibuddy.companion.noise.NoisePlayer(this, com.accountabilibuddy.companion.noise.NoiseSettingsStore(this), appScope)
        com.accountabilibuddy.companion.noise.HeadphoneWatcher(this, headphones, noise, schedulePermits = ::headphoneScheduleAllowsNow).start()
        com.accountabilibuddy.companion.noise.CallPauseWatcher(this, noise, appScope).start()
        noiseWatch = com.accountabilibuddy.companion.watch.NoiseWatchSync(this, noise, watchBridge.linkSettings, appScope, pcNoise).also { it.start() }
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
                if (it.relayOn) relaySource.syncNow() else relaySource.forget()
            }
        }
        // The header follows the devices' sessions and focus: redraw (at most every 2 s) when they change.
        appScope.launch {
            sync.received
                .debounce(2_000)
                .collect { Refresher.refresh(this@CompanionApp) }
        }
        Refresher.start(this)
        // Timers and alarms set again at every start: a force stop or a battery manager drops the app's alarms.
        runCatching { com.accountabilibuddy.companion.timers.TimerScheduler.restore(this) }
    }

    /** Android's next alarm changed (ADR 23): the wake time, the header, and this phone's file for the PCs follow. */
    fun onNextAlarmChanged() {
        refreshWakeAlarm()
        relaySoon()
        appScope.launch {
            folderSource.syncNow()
            driveSource.syncNow()
            Refresher.refresh(this@CompanionApp)
        }
    }

    // A session runs somewhere (a device's file from the last 12 hours), the timeline is pinned, or a PC's answer to a
    // start, end early or plan edit is awaited (ADR 42).
    private fun driveBusy(): Boolean {
        if (com.accountabilibuddy.companion.notify.PinnedTimelineService.isRunning) {
            return true
        }

        // A request to a PC counts for the 10 minutes it is worth answering (PcFileRemote.expire), a noise command too; a plan edit may wait for days.
        val now = java.time.Instant.now()
        val answeredSoon = setOf(SyncAction.DAY_PLAN_EDIT, SyncAction.NOISE)
        if (actions.pending.value.any { (it.kind in SyncAction.CONTROL_KINDS || it.kind in SyncAction.TASK_KINDS || it.kind in answeredSoon) && java.time.Duration.between(it.at, now) <= PcFileRemote.GIVE_UP_AFTER }) {
            return true
        }

        if (inWorkPartOrLunchExpected(now)) {
            return true
        }

        return sync.received.value.any { device ->
            java.time.Duration.between(device.file.device.writtenAt, now) <= java.time.Duration.ofHours(12) &&
                (device.file.running != null || device.file.sessions.any { it.end == null })
        }
    }

    /** How long before a planned lunch window the phone starts listening for a lunch started elsewhere. */
    private val LUNCH_LEAD: java.time.Duration = java.time.Duration.ofHours(1)

    // "Play when headphones connect" → "Only at certain times": true when the schedule allows auto-play right now.
    private fun headphoneScheduleAllowsNow(): Boolean {
        val state = store.state.value
        val now = java.time.LocalDateTime.now()
        val focus = com.accountabilibuddy.companion.notify.HeaderSource.focus(this, now)
        return com.accountabilibuddy.companion.noise.HeadphoneSchedule.allowsNow(
            headphones.settings.value, state.document.dayPlan, now,
            com.accountabilibuddy.companion.engine.DayWorkWindows.focusedToday(focus, now, java.time.ZoneId.systemDefault()),
            state.sleep::fallbackNightBefore,
        )
    }

    // Today's work parts, and lunch: running, or about to come from the plan's flexible break. Lunch is started on any
    // device, so the phone must hear of it (and be heard) within about 30 seconds, not 15 minutes, but only from an hour
    // before the planned lunch window until it ends: never all night.
    private fun inWorkPartOrLunchExpected(now: java.time.Instant): Boolean {
        val state = store.state.value
        val plan = state.document.dayPlan
        val local = java.time.LocalDateTime.ofInstant(now, java.time.ZoneId.systemDefault())
        if (lunch.isOnLunch(now)) {
            return true
        }

        val flexibleBreak = plan.flexibleBreakOn(local.toLocalDate())
        if (flexibleBreak != null && lunch.today(now) == null &&
            !local.isBefore(flexibleBreak.window.start.minus(LUNCH_LEAD)) && local.isBefore(flexibleBreak.window.end)
        ) {
            return true
        }

        // A fixed lunch period starts by itself on the PC: hear of it as quickly as a flexible one.
        val fixedLunch = plan.fixedLunchOn(local.toLocalDate())
        if (fixedLunch != null && lunch.today(now) == null &&
            !local.isBefore(fixedLunch.window.start.minus(LUNCH_LEAD)) && local.isBefore(fixedLunch.window.end)
        ) {
            return true
        }

        // The guards' own view of the day (a Budget day's windows included), not the plan's raw parts.
        return com.accountabilibuddy.companion.engine.DayWorkWindows
            .partAt(plan, local, nightFallback = state.sleep::fallbackNightBefore)?.category?.behavior?.isWork == true
    }

    private var lastUnlockSyncAt = java.time.Instant.EPOCH

    /**
     * The phone was unlocked (USER_PRESENT): one sync of every network method, so what changed on the other devices while
     * it was in a pocket shows at once. At most once a minute.
     */
    @Synchronized
    fun syncOnUnlock() {
        val now = java.time.Instant.now()
        if (java.time.Duration.between(lastUnlockSyncAt, now) < UNLOCK_SYNC_GAP) {
            return
        }

        lastUnlockSyncAt = now
        appScope.launch { runCatching { folderSource.syncNow() } }
        appScope.launch { runCatching { driveSource.syncNow() } }
        relaySoon()
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
        // The plan's nights without the alarm or the estimate, so neither moves the window it's learned from.
        val plan = state.sleep.copy(alarm = null, wakeEstimates = emptyMap()).toSchedule(state.document.dayPlan)
        learnedAlarmApp = WakeAlarm.learned(history) { at ->
            WakeAlarm.inMorningWindow(java.time.LocalDateTime.ofInstant(at, zone), plan::night)
        }
        val settings = sync.settings.value
        wakeAlarm = WakeAlarm.choose(next, history, settings.alarmApp, learnedAlarmApp, now)
        store.setPhoneAlarm(wakeAlarm?.takeIf { settings.alarmOn }?.let { java.time.LocalDateTime.ofInstant(it, zone) })
        // The estimate (ADR 40) only while the next alarm is the wake time, like the alarm itself.
        if (settings.alarmOn) {
            refreshWakeEstimate(history, settings.alarmApp ?: learnedAlarmApp, plan, now, zone)
        } else {
            store.setWakeEstimates(emptyMap())
        }
    }

    // ADR 40: the wake-up app's past alarms in the morning window, else Health Connect's measured wake times.
    private fun refreshWakeEstimate(
        history: List<AlarmSighting>,
        app: String?,
        plan: com.accountabilibuddy.companion.engine.SleepSchedule,
        now: java.time.Instant,
        zone: java.time.ZoneId,
    ) {
        val state = store.state.value
        val alarms = if (app == null) emptyList() else history
            .filter { it.app == app && !it.at.isAfter(now) }
            .map { java.time.LocalDateTime.ofInstant(it.at, zone) }
            .filter { WakeAlarm.inMorningWindow(it, plan::night) }
        val measuredWakes = state.slept.map { it.end }.filter { it.isBefore(java.time.LocalDateTime.ofInstant(now, zone)) }
        val dayPlan = state.document.dayPlan
        store.setWakeEstimates(WakeEstimate.of(alarms, measuredWakes) { morning ->
            com.accountabilibuddy.companion.engine.SmartBedtime.kindOf(morning, dayPlan::isWorkdayByWeek)
        })
    }

    /** The estimated wake times in use (ADR 40) in the sync file's shape; null when there's none. */
    private fun wakeEstimateForSync(): com.accountabilibuddy.shared.sync.SyncWakeEstimate? {
        val estimates = store.state.value.sleep.wakeEstimates
        if (estimates.isEmpty() || !sync.settings.value.alarmOn) return null
        return com.accountabilibuddy.shared.sync.SyncWakeEstimate(
            estimates[com.accountabilibuddy.companion.engine.NightKind.Workday]?.let(WakeEstimate::format),
            estimates[com.accountabilibuddy.companion.engine.NightKind.OffDay]?.let(WakeEstimate::format),
        )
    }

    /** Hands the PC the phone's file now, through Drive, from the app's own scope (the placement detector, on its way out). */
    fun syncPlacementNow() {
        sendSyncFilesSoon()
    }

    /** A work use started or ended (phase 11b): the PC hears at once (Drive), and the notice and detector follow. */
    fun workUseChanged() {
        relaySoon()
        appScope.launch {
            runCatching { driveSource.syncNow() }
            Refresher.refresh(this@CompanionApp)
        }
    }

    private var sleepOffWatch: kotlinx.coroutines.Job? = null

    /**
     * Applies a pending sleep-mode off when its wait is over (and no session runs: it is retried every 30 s then).
     * One watcher at a time; it ends when nothing is pending. A restart starts it again from the saved time.
     */
    fun watchSleepOff() {
        sleepOffWatch?.cancel()
        sleepOffWatch = appScope.launch {
            while (true) {
                val due = sleepSync.pendingOffAt() ?: break
                val wait = java.time.Duration.between(java.time.Instant.now(), due).toMillis().coerceIn(1_000L, 30_000L)
                delay(wait)
                if (sleepSync.applyPendingOffIfDue()) {
                    Refresher.refresh(this@CompanionApp)
                }
            }
        }
    }

    /** Redraws the notification, widget and nudges soon (after the full-screen card closes, a setting changes). */
    fun refreshSoon() {
        appScope.launch { Refresher.refresh(this@CompanionApp) }
    }

    /**
     * The header's refresh: reads the sync folder and Drive (which also hand over this phone's file), then redraws.
     * [onDone] gets what to say.
     */
    fun refreshNow(onDone: (String) -> Unit) {
        appScope.launch {
            val problems = listOfNotNull(
                runCatching { folderSource.syncNow() }.exceptionOrNull()?.let { "sync folder: ${it.message}" },
                runCatching { driveSource.syncNow() }.exceptionOrNull()?.let { "Drive: ${it.message}" },
                runCatching { relaySource.syncNow() }.exceptionOrNull()?.let { "relay: ${it.message}" },
            )
            Refresher.refresh(this@CompanionApp)
            val at = java.time.LocalTime.now().withSecond(0).withNano(0)
            val text = if (problems.isEmpty()) "Up to date · refreshed $at" else "Refreshed $at, but ${problems.joinToString("; ")}"
            kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.Main) { onDone(text) }
        }
    }

    /** The PC's profiles, sleep, day bar and events (L2): kept like an import, then the card and widget redraw. */
    private fun applyPcFileSettings(files: List<com.accountabilibuddy.shared.sync.SyncFile>) {
        val prefs = getSharedPreferences("pcFiles", MODE_PRIVATE)
        val name = prefs.getString("pcName", null)
        val file = com.accountabilibuddy.companion.sync.PcFileSettings.choose(files, name) ?: return
        val content = com.accountabilibuddy.companion.sync.PcFileSettings.content(file) ?: return
        val fingerprint = com.accountabilibuddy.companion.sync.PcFileSettings.fingerprint(content)
        if (prefs.getString("applied", null) == fingerprint) return
        if (applyPcSettings(content, file.device.name)) {
            prefs.edit().putString("applied", fingerprint).putString("pcName", file.device.name).apply()
        }
    }

    private fun applyPcSettings(content: JsonObject, pcName: String): Boolean {
        val data = try {
            Importer.fromPcFile(content, pcName)
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

}
