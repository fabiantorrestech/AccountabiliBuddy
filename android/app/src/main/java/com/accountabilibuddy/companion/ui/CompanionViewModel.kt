package com.accountabilibuddy.companion.ui

import android.app.Application
import android.net.Uri
import android.text.format.DateFormat
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.data.ImportException
import com.accountabilibuddy.companion.data.Importer
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.engine.DayBarModel
import com.accountabilibuddy.companion.engine.TodayInfo
import com.accountabilibuddy.companion.curbox.CurboxClient
import com.accountabilibuddy.companion.curbox.CurboxStatus
import com.accountabilibuddy.companion.engine.DayTimeline
import com.accountabilibuddy.companion.engine.DayWorkWindows
import com.accountabilibuddy.companion.health.SleepReader
import com.accountabilibuddy.companion.notify.HeaderSource
import com.accountabilibuddy.companion.usage.PhoneUsage
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import com.accountabilibuddy.companion.model.SleepSettings
import com.accountabilibuddy.companion.notify.Refresher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import com.accountabilibuddy.companion.sync.DeviceBackups
import com.accountabilibuddy.companion.sync.DriveBackups
import com.accountabilibuddy.companion.sync.EndEarlyTerms
import com.accountabilibuddy.companion.sync.JoinLink
import com.accountabilibuddy.companion.sync.PhoneSyncSettings
import com.accountabilibuddy.companion.sync.RelayAddress
import com.accountabilibuddy.shared.sync.SyncKeyCode
import com.accountabilibuddy.companion.sync.SyncShare
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.companion.engine.DayPlanValidator
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayPlanConflict
import com.accountabilibuddy.companion.model.DayPlanDraft
import com.accountabilibuddy.companion.sync.PcFileControl
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.shared.WatchLinkSettings
import com.accountabilibuddy.shared.WatchNode
import com.accountabilibuddy.shared.sync.SyncAction
import java.time.Instant
import java.time.LocalDateTime

/** The app's screens: the store's state, today's bar (redrawn every 30 seconds while on screen), importing and Health Connect. */
class CompanionViewModel(application: Application) : AndroidViewModel(application) {
    private val store = (application as CompanionApp).store
    private val sleepReader = SleepReader(application)
    private val steps = (application as CompanionApp).steps
    private val usageReader = PhoneUsageReader(application)
    val curboxClient = CurboxClient(application)
    private val watchBridge = (application as CompanionApp).watchBridge
    private val sync = (application as CompanionApp).sync
    private val folderSource = (application as CompanionApp).folderSource

    /** The Pixel Watch link (ADR 21): its settings and the watches found with the app. */
    val watchSettings: StateFlow<WatchLinkSettings> = watchBridge.settings
    val detectedWatches: StateFlow<List<WatchNode>> = watchBridge.detected

    val deviceName: String = (application as CompanionApp).deviceName

    /** A join link waiting for the user's answer (ADR 22, 46): what it would change is shown first, and nothing joins before. */
    val joinOffer = MutableStateFlow<JoinPrompt?>(null)

    private val pcRemote = (application as CompanionApp).pcRemote
    private val planFileSync = (application as CompanionApp).planFileSync

    /** Both the PC and the phone changed the day plan (ADR 19); the dialog asks which to keep. Through the sync files (ADR 42). */
    val planConflict: StateFlow<DayPlanConflict?> = planFileSync.conflict

    /** A plan edit a PC took and is holding for its wait (ADR 28), for the "change waits" sheet and Plan's quiet line. */
    val planWait: StateFlow<com.accountabilibuddy.companion.sync.PlanWait?> = planFileSync.wait

    fun planWaitSeen() = planFileSync.waitSeen()

    /** The PC the open plan conflict is with. */
    fun conflictPcName(): String? = planFileSync.conflictPcName.takeIf { planFileSync.conflict.value != null }

    /** Ending the PC's session early: the PC's terms while the dialog is open (a sync file's offer). */
    val endEarlyTerms = MutableStateFlow<EndEarlyTerms?>(null)

    /** The PC controlled through the sync files (ADR 42): the newest PC file's, or the one picked in Devices. */
    val controlPc: StateFlow<ReceivedDevice?> =
        combine(sync.received, pcRemote.chosenPcId) { received, chosen -> PcFileControl.target(received, chosen) }
            .stateIn(viewModelScope, SharingStarted.Eagerly, pcRemote.target())

    /** The PC choices for Devices (id to name, newest first) and the one picked. */
    val pcChoices: StateFlow<List<Pair<String, String>>> =
        sync.received.map { received -> PcFileControl.pcs(received).map { it.file.device.deviceId.toString() to it.file.device.name } }
            .stateIn(viewModelScope, SharingStarted.Eagerly, emptyList())
    val chosenPcId: StateFlow<String?> = pcRemote.chosenPcId

    fun choosePc(id: String?) = pcRemote.choose(id)

    /** The start or end-early request waiting for a PC's answer in its file; null for none. */
    val pcWaiting: StateFlow<SyncAction?> =
        (application as CompanionApp).actions.pending.map { pending -> pending.lastOrNull { it.kind in SyncAction.CONTROL_KINDS } }
            .stateIn(viewModelScope, SharingStarted.Eagerly, null)

    fun cancelPcWaiting(pcId: java.util.UUID? = null) = pcRemote.cancelWaiting(pcId)

    private val now = MutableStateFlow(LocalDateTime.now())

    /** Runs only while the app is on screen; the notification and widget have their own refresh. */
    private var ticker: Job? = null

    val clock = ClockFormat(is24Hour = false) /* always 12-hour AM/PM (project convention) */
    private val builder = DayBarBuilder(clock)
    val state: StateFlow<CompanionState> = store.state

    /** Null while usage access isn't granted. */
    val phoneUsage = MutableStateFlow<PhoneUsage?>(null)

    // The test session too, so its header line comes and goes at once.
    val model: StateFlow<DayBarModel> = combine(store.state, now, phoneUsage, getApplication<CompanionApp>().testSession.window) { state, time, _, _ -> build(state, time) }
        .stateIn(viewModelScope, SharingStarted.Eagerly, build(store.state.value, now.value))

    /** A message after an import or another action, shown once. */
    val message = MutableStateFlow<String?>(null)

    /**
     * A route asked for by MainActivity's `ab.open` extra (`settings/devices`), waiting for the shell to take it. The shell only
     * navigates to it: opening a page never starts, changes or unlocks anything.
     */
    val openRequest = MutableStateFlow<String?>(null)

    init {
        // A PC's answer through the sync files (ADR 42) shows once too.
        viewModelScope.launch {
            pcRemote.news.filterNotNull().collect { news ->
                message.value = news
                pcRemote.clearNews()
            }
        }
        // A PC's end-early offer opens the dialog; it closes when the offer is answered or runs out.
        viewModelScope.launch {
            pcRemote.offer.collect { offer ->
                if (offer != null) {
                    endEarlyTerms.value = offer.terms()
                } else if (endEarlyTerms.value?.allowedFrom != null) {
                    endEarlyTerms.value = null
                }
            }
        }
    }
    val passwordRequest = MutableStateFlow<PasswordRequest?>(null)
    val importing = MutableStateFlow(false)
    val health = MutableStateFlow(HealthStatus.Checking)

    val curbox = MutableStateFlow(CurboxStatus())

    val healthPermissions: Set<String> get() = sleepReader.permissions

    /**
     * The app came on screen: catch up, then keep the bar's now line (every 30 s) and phone use (every 5 min)
     * current until [onStop]. Curbox is asked only when Settings shows it.
     */
    fun onStart() {
        (getApplication<CompanionApp>()).appOnScreen = true
        now.value = LocalDateTime.now()
        checkHealth()
        refreshPhoneUsage()
        refreshSteps()
        ticker?.cancel()
        ticker = viewModelScope.launch {
            var ticks = 0
            while (true) {
                delay(30_000)
                now.value = LocalDateTime.now()
                pcRemote.expire()
                if (++ticks % 10 == 0) {
                    refreshPhoneUsage()
                }
                // Steps: only while on screen (Health Connect's rule), and at most every few minutes (StepsStore.MIN_GAP).
                if (ticks % 6 == 0) {
                    refreshSteps()
                }
            }
        }
    }

    fun onStop() {
        (getApplication<CompanionApp>()).appOnScreen = false
        ticker?.cancel()
        ticker = null
    }

    /**
     * A join link was opened (ADR 22): from the camera, a message, a web page or another app, so it is never trusted.
     * Works out what joining would change and asks; a link that would leave the current group asks twice.
     */
    fun offerJoin(link: JoinLink) {
        val currentKey = sync.key.value
        val linkKey = SyncKeyCode.parse(link.code)
        val settings = sync.settings.value
        joinOffer.value = JoinPrompt(
            link = link,
            leavesCurrentGroup = currentKey != null && !currentKey.sameGroupAs(linkKey),
            newRelay = link.relayUrl != null && !(settings.relayOn && settings.relayUrl == link.relayUrl),
            relayOnInternet = link.relayUrl != null && !RelayAddress.isLocalNetwork(link.relayUrl) &&
                !(settings.relayOn && settings.relayUrl == link.relayUrl),
        )
    }

    /**
     * Scanned or pasted text (ADR 57): offers the join when it is a join link, exactly as a link opened from outside does.
     * False when it isn't one, so the caller can try it as a bare code or say so.
     */
    fun offerJoinText(text: String): Boolean {
        val link = JoinLink.parse(text) ?: return false
        offerJoin(link)
        return true
    }

    fun cancelJoin() {
        joinOffer.value = null
    }

    /** The first answer: a link that leaves the current group goes on to the second question; any other joins now. */
    fun continueJoin() {
        val prompt = joinOffer.value ?: return
        if (prompt.leavesCurrentGroup && !prompt.confirmingLeave) {
            joinOffer.value = prompt.copy(confirmingLeave = true)
            return
        }

        confirmJoin()
    }

    /** Joins the group; this phone's file goes out with the next Drive or folder sync. */
    private fun confirmJoin() {
        val prompt = joinOffer.value ?: return
        joinOffer.value = null
        val offer = prompt.link
        // No escape hatches (ADR 54): leaving a group, and adding an unlinked phone again (the PC's rule too), wait for a focus session to end.
        if (prompt.leavesCurrentGroup || sync.unlinked.value != null) {
            sync.removalLockedReason()?.let {
                message.value = it
                return
            }
        }
        if (!sync.join(offer.code)) {
            message.value = "That link code didn't work. Show it again on ${offer.pcName}."
            return
        }

        // A link from a device that uses its own relay (ADR 46) sets this phone's relay up the same way.
        if (offer.relayUrl != null) {
            // Pointing a relay that is already on somewhere else is refused during a focus session (the same lock as the switch).
            val refused = sync.saveSettings(
                sync.settings.value.copy(relayOn = true, relayUrl = offer.relayUrl, relayPin = offer.relayPin, relayAdmission = offer.relayAdmission),
            )
            message.value = refused ?: joinedMessage("Joined ${offer.pcName}'s devices, through the relay.")
            return
        }

        message.value = joinedMessage("Joined ${offer.pcName}'s devices.")
    }

    // A code gives the group's key and no way to reach anyone: while no other device is held, say what is missing.
    private fun joinedMessage(joined: String): String =
        if (sync.received.value.isEmpty()) "$joined ${com.accountabilibuddy.companion.sync.SyncReachText.whileNobodyHeard(sync.settings.value)}" else joined

    /** One of the PC's sync QR codes was scanned: e.g. "2 of 3 scanned", then "Synced Work PC". */
    fun scanSyncCode(link: String) {
        viewModelScope.launch(Dispatchers.IO) { message.value = sync.onQrLink(link) }
    }

    /** Sync files (*.absync) opened or shared with the app. */
    fun openSyncFiles(uris: List<Uri>) {
        val app = getApplication<CompanionApp>()
        viewModelScope.launch(Dispatchers.IO) {
            val results = uris.map { uri ->
                val bytes = SyncShare.read(app, uri) ?: return@map "That file couldn't be read."
                sync.acceptSealed(bytes, SyncDeviceCard.METHOD_FILE)
                    ?: "Synced ${sync.received.value.firstOrNull { it.sealed === bytes }?.file?.device?.name ?: "your other device"}"
            }
            message.value = if (results.size == 1) results[0] else results.distinct().joinToString(" · ")
        }
    }

    fun showMessage(text: String) {
        message.value = text
    }

    /** The running session's checklist (ADR 24): the newest sync file's open session. */
    val runningTasks: StateFlow<com.accountabilibuddy.companion.sync.RunningTasks?> =
        sync.received.map { received -> com.accountabilibuddy.companion.sync.RunningTasks.of(received, java.time.Instant.now()) }
            .stateIn(viewModelScope, SharingStarted.Eagerly, null)

    /** The open come-backs (ADR 30, 48): the list every device shares, merged from the received files; edited on Today. */
    val comebacks: StateFlow<com.accountabilibuddy.companion.sync.OpenComebacks?> =
        (application as CompanionApp).sharedLists.let { lists ->
            combine(lists.parked, sync.received) { parked, received ->
                com.accountabilibuddy.companion.sync.OpenComebacks.of(parked, received, java.time.Instant.now())
            }.stateIn(
                viewModelScope, SharingStarted.Eagerly,
                com.accountabilibuddy.companion.sync.OpenComebacks.of(lists.parked.value, sync.received.value, java.time.Instant.now()),
            )
        }

    /** The user's Beeminder goals (ADR 43) from the newest PC sync file that carries them; read-only on Today. */
    val beeminder: StateFlow<com.accountabilibuddy.companion.sync.BeeminderGoals?> =
        sync.received.map { received -> com.accountabilibuddy.companion.sync.BeeminderGoals.of(received, java.time.Instant.now()) }
            .stateIn(viewModelScope, SharingStarted.Eagerly, null)

    /** The Devices tab (ADR 22): the other devices' parts, the group's key and the phone's method switches. */
    val syncReceived = sync.received
    val syncAllReceived = sync.allReceived
    val syncKey = sync.key
    val syncSettings = sync.settings

    fun syncLinkCode(): String? = sync.linkCode()

    fun ownQrLinks(): List<String> = sync.ownQrLinks()

    /**
     * Saves the sharing settings. A change that turns a way of syncing off or elsewhere is refused during a focus session, and says why; turning
     * off the last way of syncing between sessions is held for 10 minutes ([syncOffPending]) and says when.
     */
    fun saveSyncSettings(settings: PhoneSyncSettings) {
        sync.saveSettings(settings)?.let { message.value = it }
    }

    /** The held turn-off of the last way of syncing, or null: the way's row says when it turns off and offers Cancel. */
    val syncOffPending: kotlinx.coroutines.flow.StateFlow<com.accountabilibuddy.companion.sync.PendingSyncOff?> =
        sync.offWait?.pending ?: kotlinx.coroutines.flow.MutableStateFlow<com.accountabilibuddy.companion.sync.PendingSyncOff?>(null)

    /** Whether turning [method] off would wait 10 minutes (it is the only way that counts), so the page asks first. */
    fun turnOffWaits(method: com.accountabilibuddy.companion.sync.SyncMethod): Boolean = sync.turnOffWaits(method)

    /** Whether pointing [method] somewhere else is refused now (it is the only way that counts and has no problem), so the page says so. */
    fun repointWaits(method: com.accountabilibuddy.companion.sync.SyncMethod): Boolean = sync.repointWaits(method)

    /** Whether another device's file has come through [method] from where it points now. */
    fun hasDelivered(method: com.accountabilibuddy.companion.sync.SyncMethod): Boolean = sync.hasDelivered(method)

    /** Whether [method] counts as a way of syncing now (it has brought another device's file), so the page can say what changing it does. */
    fun countsAsWay(method: com.accountabilibuddy.companion.sync.SyncMethod): Boolean = sync.counts(method)

    /** Cancels the held turn-off (any time). */
    fun cancelSyncOff() = sync.cancelPendingSyncOff()

    /** Apps seen setting alarms (id to name, newest first) for the wake-up alarm picker. */
    fun alarmApps(): List<Pair<String, String>> {
        val app = getApplication<CompanionApp>()
        return app.alarmHistory.history.reversed().mapNotNull { it.app }.distinct().map { id ->
            id to (runCatching { app.packageManager.getApplicationLabel(app.packageManager.getApplicationInfo(id, 0)).toString() }.getOrNull() ?: id)
        }
    }

    /** The app learned as the wake-up alarm, or null. */
    fun learnedAlarmApp(): String? = getApplication<CompanionApp>().learnedAlarmApp

    /** The sync folder's last result (ADR 22, way B). */
    val folderStatus = folderSource.status

    /** The folder picked for syncing (e.g. the one Autosync keeps in step with Drive): kept across restarts, then synced. */
    fun chooseSyncFolder(uri: android.net.Uri) {
        val flags = android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION or android.content.Intent.FLAG_GRANT_WRITE_URI_PERMISSION
        try {
            getApplication<Application>().contentResolver.takePersistableUriPermission(uri, flags)
        } catch (e: SecurityException) {
            message.value = "That folder can't be kept: choose a folder on the phone's storage."
            return
        }

        // Choosing another folder while one is in use is refused during a focus session (turning the folder on is not).
        sync.saveSettings(sync.settings.value.copy(folderOn = true, folderUri = uri.toString()))?.let { message.value = it }
    }

    private val pause = (application as CompanionApp).pause

    /** The phone's master switch and car pause. */
    val pauseState = pause.state

    fun requestPause(length: com.accountabilibuddy.companion.pause.PauseLength, waitMinutes: Int, untilTime: java.time.LocalTime) {
        pause.requestOff(length, waitMinutes, untilTime)
        message.value = "Turning off in $waitMinutes min unless you cancel."
        refreshSoon()
    }

    fun turnPauseOff() {
        pause.turnOn()
        message.value = "AccountabiliBuddy is on."
        refreshSoon()
    }

    fun setCarPause(on: Boolean) {
        pause.setCarPause(on)
    }

    private fun refreshSoon() {
        viewModelScope.launch { com.accountabilibuddy.companion.notify.Refresher.refresh(getApplication()) }
    }

    private val driveSource = (application as CompanionApp).driveSource
    private val driveSignIn = (application as CompanionApp).driveSignIn

    private val driveBackups = DriveBackups(driveSignIn)

    /** The "Remove this device" dialog (ADR 54, 55); null when closed. */
    val removeDevice = RemoveDeviceFlow(application, sync, driveBackups, viewModelScope)

    /** The "Restore from an older device" dialog (ADR 55); null when closed. */
    val restoreBackup = RestoreBackupFlow(application, sync, (application as CompanionApp).backupOpener, driveBackups, viewModelScope)

    /** Set while this phone was removed from its synced devices, or cut off, and waits for the user's choice (ADR 54). */
    val unlinked = sync.unlinked

    /** The devices removed from the group, whose kept data the Devices tab lists as "(removed)". */
    val removedDevices = sync.removedDevices

    /** Why devices can't be removed, left, or this phone started from scratch right now (a focus session the user started); null when they can. */
    fun removalLockedReason(): String? = sync.removalLockedReason()

    /**
     * [removalLockedReason] as state, looked at again when the other devices' files change and every 15 seconds (a session ends by the
     * clock, with no file to say so), so the Unlinked screen's buttons don't stay off after the lock lifts.
     */
    val removalLocked: StateFlow<String?> =
        combine(sync.received, flow { while (true) { emit(Unit); delay(LOCK_RECHECK_MS) } }) { _, _ -> sync.removalLockedReason() }
            .distinctUntilChanged()
            .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), sync.removalLockedReason())

    /**
     * The guard lock (the guard settings are view-only while a focus session runs): the same session check as [removalLocked], with the
     * session's end time for the banner. Looked at again when the other devices' files or the test session change and every few
     * seconds, so a guard page that is open unlocks by itself when the session ends.
     */
    val guardsLock: StateFlow<com.accountabilibuddy.companion.placement.GuardsLock?> =
        combine(sync.received, getApplication<CompanionApp>().testSession.window, flow { while (true) { emit(Unit); delay(GUARDS_RECHECK_MS) } }) { _, _, _ ->
            com.accountabilibuddy.companion.placement.GuardsLock.at(getApplication(), java.time.Instant.now())
        }
            .distinctUntilChanged()
            .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), com.accountabilibuddy.companion.placement.GuardsLock.at(getApplication(), java.time.Instant.now()))

    fun openRemoveDevice(deviceId: java.util.UUID) = removeDevice.open(deviceId)

    /** "Continue unsynced here" (ADR 54): leaves the group, keeps all data. True when done; else the reason is shown. */
    fun continueUnsynced(): Boolean {
        val reason = sync.continueUnsynced()
        if (reason != null) {
            message.value = reason
            return false
        }

        message.value = "You can link again from Settings › Devices & sync."
        return true
    }

    init {
        // A start from scratch that never finished (ADR 54) says so once, so the user can ask again from the Unlinked screen.
        com.accountabilibuddy.companion.setup.StartOver.takeNotice(application)?.let { message.value = it }
    }

    /**
     * "Start from scratch" (ADR 54): after the typed phrase and only when no focus session the user started runs, a local copy of the
     * app's data files is kept, the watch is told to unlink (it then asks what to do with its data), a marker is left and the app
     * restarts; the new process clears the app's files and preferences before anything opens them
     * ([com.accountabilibuddy.companion.setup.StartOver]) and setup opens as on a first run.
     */
    fun startFromScratch(typed: String) {
        sync.removalLockedReason()?.let {
            message.value = it
            return
        }

        if (typed.trim().lowercase() != com.accountabilibuddy.companion.setup.StartOver.PHRASE) {
            message.value = "Type \"${com.accountabilibuddy.companion.setup.StartOver.PHRASE}\" to confirm."
            return
        }

        val app = getApplication<CompanionApp>()
        viewModelScope.launch {
            val problem = withContext(Dispatchers.IO) { com.accountabilibuddy.companion.setup.StartOver.prepare(app) }
            if (problem != null) {
                message.value = "Start from scratch didn't happen: $problem. Nothing was cleared."
                return@launch
            }

            // The watch hears first (W4): it asks its wearer whether to forget what it shows. Waits a few seconds at most.
            withContext(Dispatchers.IO) {
                kotlinx.coroutines.withTimeoutOrNull(WATCH_WAIT_MS) { runCatching { watchBridge.unlinkWatchAndWait() } }
                // The periodic Drive-folder work is WorkManager's, in a folder the clearing leaves alone: it ends here.
                runCatching { androidx.work.WorkManager.getInstance(app).cancelAllWork() }
            }
            try {
                com.accountabilibuddy.companion.setup.StartOver.restart(app)
            } catch (e: Exception) {
                com.accountabilibuddy.companion.setup.StartOver.cancel(app)
                message.value = "Start from scratch didn't happen: the app couldn't restart. Nothing was cleared."
            }
        }
    }

    /** Google Drive's last result (ADR 26). */
    val driveStatus = driveSource.status

    private val relaySource = (application as CompanionApp).relaySource

    /** The own relay's last result (ADR 46). */
    val relayStatus = relaySource.status

    fun syncRelayNow() {
        viewModelScope.launch {
            relaySource.syncNow()
            message.value = relaySource.status.value.problem ?: "Synced with the relay."
        }
    }

    /** Starts the Drive sign-in: null when access was granted at once (Drive is turned on), else Google's screen to show. */
    suspend fun beginDriveSignIn(): android.app.PendingIntent? =
        when (val step = driveSignIn.begin()) {
            is com.accountabilibuddy.companion.sync.DriveSignInStep.Token -> {
                turnDriveOn()
                null
            }
            is com.accountabilibuddy.companion.sync.DriveSignInStep.Ask -> step.intent
            is com.accountabilibuddy.companion.sync.DriveSignInStep.Failed -> {
                message.value = step.message
                null
            }
        }

    /** Google's sign-in screen answered. */
    fun finishDriveSignIn(data: android.content.Intent?) {
        if (driveSignIn.tokenFrom(data) != null) turnDriveOn()
        else message.value = "Google Drive wasn't allowed. Settings › Devices & sync › Google Drive › \"How to set up Google Drive\" lists what the Google Cloud project needs."
    }

    private fun turnDriveOn() {
        message.value = sync.saveSettings(sync.settings.value.copy(driveOn = true)) ?: "Signed in: this phone syncs through Google Drive."
    }

    fun syncDriveNow() {
        viewModelScope.launch {
            driveSource.syncNow()
            message.value = driveSource.status.value.problem ?: "Synced with Google Drive."
        }
    }

    fun syncFolderNow() {
        viewModelScope.launch {
            folderSource.syncNow()
            message.value = folderSource.status.value.problem ?: "Synced the folder."
        }
    }

    /** Joins the group from a typed link code; this phone's file goes out with the next Drive or folder sync. */
    fun joinWithCode(code: String): Boolean {
        val typed = code.trim()
        val target = SyncKeyCode.parse(typed) ?: return false

        // Adding an unlinked phone again waits for a focus session to end, as on the PC (the code was fine: no "not a link code" line).
        if (sync.unlinked.value != null) {
            sync.removalLockedReason()?.let {
                message.value = it
                return true
            }
        }

        // A different group than the one this phone is in: the same question and second confirmation as a join link.
        val currentKey = sync.key.value
        if (currentKey != null && !currentKey.sameGroupAs(target)) {
            joinOffer.value = JoinPrompt(
                link = JoinLink(typed, "your other device"),
                leavesCurrentGroup = true,
                newRelay = false,
                relayOnInternet = false,
                typedCode = true,
            )
            return true
        }

        if (!sync.join(typed)) {
            return false
        }

        message.value = joinedMessage("Joined your devices.")
        return true
    }

    fun leaveSyncGroup() {
        // No escape hatches during a focus session the user started (ADR 54).
        sync.removalLockedReason()?.let {
            message.value = it
            return
        }

        // The relay's copy of this phone's file goes first, while the group's key is still here (ADR 46).
        relaySource.leaveGroup()
        sync.leave()
        message.value = "Left your device group. What came from your other devices is gone from this phone."
    }

    /** Opens the share sheet with this phone's sync file and the ones it carries. */
    fun shareSyncFiles(context: android.content.Context) {
        val files = sync.filesToCarry(SyncDeviceCard.METHOD_FILE)
        if (files.isEmpty()) {
            message.value = "This phone isn't linked to your other devices yet."
            return
        }

        SyncShare.share(context, files)
    }

    fun findWatches() = watchBridge.findWatches()

    fun linkWatch(watch: WatchNode) {
        watchBridge.linkWatch(watch)
        message.value = "Linked ${watch.name}. Open AccountabiliBuddy on the watch."
    }

    fun unlinkWatch() = watchBridge.unlinkWatch()

    fun setWatchDataLayer(on: Boolean) = watchBridge.setDataLayer(on)

    fun setWatchRelay(on: Boolean) = watchBridge.setRelay(on)

    fun startOnPc(profileId: String?, minutes: Int?, countUp: Boolean) {
        message.value = pcRemote.start(profileId, minutes, countUp) ?: "Sent to ${pcRemote.target()?.file?.device?.name ?: "the PC"}; it starts when the PC next syncs."
    }

    fun beginEndEarly(pcId: java.util.UUID? = null) {
        pcRemote.beginEndEarly(pcId)?.let { message.value = it }
    }

    fun cancelEndEarly() {
        endEarlyTerms.value = null
        pcRemote.cancelEndEarly()
    }

    /** "Confirm" during the PC's wait (ADR 66): the dialog closes; the phone sends the end by itself when the wait is over. */
    fun armEndEarly(reason: String) {
        val error = pcRemote.armEndEarly(reason)
        endEarlyTerms.value = null
        message.value = error ?: "Confirmed: it ends by itself when the wait is over. Cancel from Today or the notification."
    }

    fun finishEndEarly(reason: String) {
        // Only the reason's length goes to the PC (ADR 42); the dialog closes with its answer.
        pcRemote.endEarly(reason)?.let { error ->
            endEarlyTerms.value = null
            message.value = error
        }
    }

    /** The day plan open in the editor; kept here so it survives rotation. Null when the editor is closed. */
    val planDraft = MutableStateFlow<DayPlanDraft?>(null)

    /**
     * Opens the editor on the plan shown, building on the PC plan the phone has now (or on the waiting edit's
     * starting point). Null when it opened, otherwise why not.
     */
    /** The editor's first tab when it opens (2: one-off days, from the pause switch's "Plan off days"). */
    var planEditorTab: Int = 0
        private set

    /**
     * Why the plan can't be changed right now, or null: the plan rests during a focus session the phone tracks (the PC's rule for
     * its Plan page, and the same live check the guard settings and the removal lock use), the phone's own test session
     * included. Every way into a plan change asks this first, so no route gets around it.
     */
    fun planLockedReason(): String? =
        com.accountabilibuddy.companion.placement.GuardsLock.at(getApplication(), Instant.now())?.planSentence()

    /** "Change a day" (ADR 37): opens it unless the plan rests. Null when it opened, otherwise why not. */
    fun openPlanDay(): String? {
        planLockedReason()?.let { return it }
        planDayOpen.value = true
        return null
    }

    fun openPlanEditor(startTab: Int = 0): String? {
        planLockedReason()?.let { return it }
        planEditorTab = startTab
        val state = store.state.value
        val pending = state.pendingPlan
        val baseVersion = pending?.baseVersion ?: state.pcPlanVersion
            ?: return "Update AccountabiliBuddy on the PC to 0.1.48 or newer and let it sync, then open the plan again."
        planDraft.value = DayPlanDraft(state.document.dayPlan, state.document.dayPlan, baseVersion, pending?.basePlan ?: state.pcDayPlan)
        return null
    }

    fun updatePlanDraft(plan: DayPlan) {
        planDraft.value = planDraft.value?.copy(plan = plan)
    }

    fun closePlanEditor() {
        planDraft.value = null
    }

    /**
     * Saves the draft: checked by the PC's rules first, then kept and sent to the PC (now, or when it can be
     * reached). While it waits, the phone shows it.
     */
    fun savePlanDraft(): Boolean {
        val draft = planDraft.value ?: return false
        // A session that started while the editor was open: the draft stays, nothing is saved.
        planLockedReason()?.let {
            message.value = it
            return false
        }

        DayPlanValidator.validate(draft.plan)?.let {
            message.value = it
            return false
        }

        store.savePendingPlan(draft.plan, Instant.now(), draft.baseVersion, draft.basePlan)
        planDraft.value = null
        message.value = when {
            pcRemote.target() != null -> "Saved; it goes to ${pcRemote.target()?.file?.device?.name} through the sync files."
            else -> "Saved; it goes to the PC once a PC file is here (Settings › Devices & sync › Google Drive)."
        }
        return true
    }

    /** "Plan a day" (ADR 37) is open. */
    val planDayOpen = MutableStateFlow(false)

    /** Every device's blocks for "Plan a day": Curbox here, Cold Turkey on the PCs (from their sync files). */
    fun planDayBlocks(): List<PlanDayBlock> {
        val app = getApplication<CompanionApp>()
        val own = com.accountabilibuddy.companion.blocks.BlockGuard.lastBlockers(app).orEmpty().map {
            PlanDayBlock(com.accountabilibuddy.companion.model.BlockRef(com.accountabilibuddy.companion.model.BlockRef.PHONE, it.kind, it.id, it.name), "This phone", it.on)
        }
        val pcs = app.sync.received.value.filter { it.file.device.kind == com.accountabilibuddy.shared.sync.SyncDeviceCard.KIND_PC }.flatMap { device ->
            device.file.blockers.orEmpty().map {
                PlanDayBlock(com.accountabilibuddy.companion.model.BlockRef("pc:${device.file.device.deviceId}", it.kind, it.id, it.name), device.file.device.name, it.on)
            }
        }
        return own + pcs
    }

    /**
     * Saves "Plan a day"'s one-off days like an Edit plan change (ADR 19): kept and sent to the PC. Null when saved,
     * else why not.
     */
    fun savePlannedDays(request: com.accountabilibuddy.companion.engine.DayPlanRequest): String? {
        openPlanEditor()?.let { return it }
        val draft = planDraft.value ?: return "The plan couldn't be opened."
        updatePlanDraft(com.accountabilibuddy.companion.engine.DayPlanner.apply(draft.plan, request))
        return if (savePlanDraft()) null else message.value ?: "Not saved."
    }

    /**
     * Sets when Workdays' or Off-Days' blocks come back on (ADR 37); saved like an Edit plan change and sent to the
     * PC. Null when saved, else why not.
     */
    fun setReenableTime(offDay: Boolean, time: java.time.LocalTime): String? {
        openPlanEditor()?.let { return it }
        val plan = planDraft.value?.plan ?: return "The plan couldn't be opened."
        // Every template of that kind (a workday one has work, or is the Workday template), as the PC's setup assistant does.
        updatePlanDraft(plan.copy(templates = plan.templates.map { named ->
            if (plan.isWorkdayTemplate(named.id) != offDay) named.copy(template = named.template.copy(blocks = named.template.rules.copy(reenableAt = time))) else named
        }))
        return if (savePlanDraft()) null else message.value ?: "Not saved."
    }

    /**
     * "This is week N of M" on Today (ADR 47): moves week 1's start so today's week is week [week], saved like an Edit plan
     * change and sent to the PC, which decides (more work at once, less waits). Null when saved, else why not.
     */
    fun setThisWeek(week: Int): String? {
        openPlanEditor()?.let { return it }
        val plan = planDraft.value?.plan ?: return "The plan couldn't be opened."
        updatePlanDraft(com.accountabilibuddy.companion.engine.DayPlanEditing.withThisWeek(plan, week, java.time.LocalDate.now()))
        return if (savePlanDraft()) null else message.value ?: "Not saved."
    }

    fun discardDayPlanEdit() {
        // Dropping an edit moves the phone back to the PC's plan: also a plan change, so it rests with the plan.
        planLockedReason()?.let {
            message.value = it
            return
        }

        store.clearPendingPlan(null)
        message.value = "Edit discarded; showing the PC's plan."
    }

    fun useMyPlan() {
        planFileSync.useMine()
    }

    fun keepPcPlan() {
        planFileSync.keepPc()
    }

    /** Today's phone use and the part of it inside planned focus (the bar's day). */
    fun refreshPhoneUsage() {
        viewModelScope.launch {
            if (!usageReader.hasAccess()) {
                phoneUsage.value = null
                return@launch
            }

            val state = store.state.value
            val day = builder.dayAt(LocalDateTime.now(), state.dayBar, state.sleep, state.document.dayPlan)
            val focus = DayWorkWindows.workWindows(getApplication<CompanionApp>().effectivePlan(state), day, LocalDateTime.now(), nightFallback = state.sleep::fallbackNightBefore).map { it.window }
            phoneUsage.value = runCatching { withContext(Dispatchers.IO) { usageReader.read(day, focus) } }.getOrNull()
        }
    }

    fun refreshCurbox() {
        viewModelScope.launch { curbox.value = curboxClient.read() }
    }

    fun import(uri: Uri, password: String? = null) {
        viewModelScope.launch {
            importing.value = true
            try {
                val text = withContext(Dispatchers.IO) {
                    getApplication<Application>().contentResolver.openInputStream(uri)?.use { it.readBytes().toString(Charsets.UTF_8) }
                } ?: throw ImportException("The file couldn't be read.")

                if (password == null && Importer.needsPassword(text)) {
                    passwordRequest.value = PasswordRequest(uri)
                    return@launch
                }

                // PBKDF2 with 600,000 rounds takes a few seconds on a phone.
                val data = withContext(Dispatchers.Default) { Importer.read(text, password) }
                store.import(data)
                passwordRequest.value = null
                message.value = "Imported ${data.document.profiles.size} profiles and the day plan" +
                    (if (data.sleep != null) ", sleep times" else "") +
                    (if (data.events.isNotEmpty()) " and ${data.events.size} events" else "") + "."
                refreshOutside()
            } catch (e: ImportException) {
                if (password != null && e.message?.startsWith("Wrong password") == true) {
                    passwordRequest.value = PasswordRequest(uri, wrongPassword = true)
                } else {
                    passwordRequest.value = null
                    message.value = e.message
                }
            } finally {
                importing.value = false
            }
        }
    }

    fun cancelPassword() {
        passwordRequest.value = null
    }

    fun saveSleep(settings: SleepSettings) {
        // An edit here: stamped, so it goes to the PCs and wins over an older one there (ADR 35).
        // Turning sleep mode off is held (10 min) or refused during a focus session; the note says which.
        (getApplication<Application>() as com.accountabilibuddy.companion.CompanionApp).sleepSync.saveEdit(settings)?.let { message.value = it }
        refreshOutside()
    }

    fun setTimelineNotification(on: Boolean) {
        store.setTimelineNotification(on)
        refreshOutside()
    }

    fun clearImport() {
        store.clearImport()
        message.value = "Imported data removed."
        refreshOutside()
    }

    fun onHealthPermissionResult() {
        checkHealth(readAfter = true)
    }


    /**
     * The steps permission dialog closed: read at once if it was granted. If not, nothing turns off (the switch stays on and the card
     * says what is missing, with a tap to ask again), so a check can never race the dialog.
     */
    fun onStepsPermissionResult() = refreshSteps(force = true)

    /** Reads today's steps (the store decides whether one is due; the sync worker does the same in the background). */
    fun refreshSteps(force: Boolean = false) {
        viewModelScope.launch {
            val failure = steps.readIfDue(force = force)
            if (failure != null) message.value = failure
        }
    }

    /** Reads last night's sleep again (when connected). */
    fun refreshSleep() = checkHealth(readAfter = true)

    fun refreshOutside() {
        viewModelScope.launch(Dispatchers.Default) { Refresher.refresh(getApplication()) }
    }

    private fun checkHealth(readAfter: Boolean = true) {
        viewModelScope.launch {
            health.value = when {
                !sleepReader.isAvailable() -> HealthStatus.NotAvailable
                runCatching { sleepReader.hasPermission() }.getOrDefault(false) -> HealthStatus.Connected
                else -> HealthStatus.NeedsPermission
            }
            if (readAfter && health.value == HealthStatus.Connected) {
                runCatching { withContext(Dispatchers.IO) { sleepReader.recentSleep() } }
                    .onSuccess { slept ->
                        // Saved, and drawn on the card and widget, only when Health Connect has something new.
                        if (slept != store.state.value.slept) {
                            store.setSlept(slept)
                            refreshOutside()
                        }
                    }
                    .onFailure { message.value = "Couldn't read sleep: ${it.message}" }
            }
        }
    }

    private fun build(state: CompanionState, time: LocalDateTime): DayBarModel {
        val app = getApplication<CompanionApp>()
        val focus = HeaderSource.focus(app, time)
        val header = HeaderSource.header(app, time, clock, phoneUsage.value?.total?.toMinutes()?.toInt(), focus)
        val bar = builder.build(time, state.document, state.events, state.dayBar, state.sleep, state.slept, header, focus, (app.lunch.today()?.let { com.accountabilibuddy.companion.model.LocalWindow(java.time.LocalDateTime.ofInstant(it.start, java.time.ZoneId.systemDefault()), java.time.LocalDateTime.ofInstant(it.end, java.time.ZoneId.systemDefault())) }), plan = app.effectivePlan(state))
        // Today's headline lines and the ring's numbers, from the same focus as the bar.
        val notWorking = com.accountabilibuddy.companion.nudge.NudgeStore(app).load().dayOff == time.toLocalDate()
        val effective = app.effectivePlan(state)
        val calendarLines = com.accountabilibuddy.companion.calendar.CalendarTodayLines.of(state.document.dayPlan, effective, time.toLocalDate())
        return bar.copy(today = TodayInfo.of(effective, time, focus, clock, state.sleep::fallbackNightBefore, notWorking).copy(calendar = calendarLines))
    }

    private companion object {
        // How long "Start from scratch" waits for the watch to be told before it goes on.
        const val WATCH_WAIT_MS = 6_000L

        // How often the focus-session lock is looked at again for the screens that show it.
        const val LOCK_RECHECK_MS = 15_000L
        const val GUARDS_RECHECK_MS = 5_000L
    }
}
