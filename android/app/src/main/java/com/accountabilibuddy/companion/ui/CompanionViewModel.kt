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
import com.accountabilibuddy.companion.curbox.CurboxClient
import com.accountabilibuddy.companion.curbox.CurboxStatus
import com.accountabilibuddy.companion.engine.DayTimeline
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
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import com.accountabilibuddy.companion.link.EndEarlyTerms
import com.accountabilibuddy.companion.link.LinkStatus
import com.accountabilibuddy.companion.link.LinkedPc
import com.accountabilibuddy.companion.link.PairingLink
import com.accountabilibuddy.companion.sync.JoinLink
import com.accountabilibuddy.companion.sync.PhoneSyncSettings
import com.accountabilibuddy.companion.sync.SyncShare
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.companion.engine.DayPlanValidator
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayPlanConflict
import com.accountabilibuddy.companion.model.DayPlanDraft
import com.accountabilibuddy.shared.WatchLinkSettings
import com.accountabilibuddy.shared.WatchNode
import com.accountabilibuddy.shared.link.PcOffer
import java.time.Instant
import java.time.LocalDateTime

/** The app's screens: the store's state, today's bar (redrawn every 30 seconds while on screen), importing and Health Connect. */
class CompanionViewModel(application: Application) : AndroidViewModel(application) {
    private val store = (application as CompanionApp).store
    private val sleepReader = SleepReader(application)
    private val usageReader = PhoneUsageReader(application)
    val curboxClient = CurboxClient(application)
    private val link = (application as CompanionApp).link
    private val watchBridge = (application as CompanionApp).watchBridge
    private val sync = (application as CompanionApp).sync
    private val folderSource = (application as CompanionApp).folderSource

    /** The Pixel Watch link (ADR 21): its settings and the watches found with the app. */
    val watchSettings: StateFlow<WatchLinkSettings> = watchBridge.settings
    val detectedWatches: StateFlow<List<WatchNode>> = watchBridge.detected

    /** The linked PC (ADR 14) and its session. */
    val linkStatus: StateFlow<LinkStatus> = link.status
    val pairedPc: StateFlow<LinkedPc?> = link.pairedPc
    val deviceName: String = (application as CompanionApp).deviceName

    /** A pairing link opened from the PC's QR code, waiting for the user to compare the code and confirm. */
    val pairingOffer = MutableStateFlow<PairingLink?>(null)

    /** A scanned link code waiting for "Join <PC name>'s devices?" (ADR 22). */
    val joinOffer = MutableStateFlow<JoinLink?>(null)
    val pairing = MutableStateFlow(false)

    /** Both the PC and the phone changed the day plan (ADR 19); the dialog asks which to keep. */
    val planConflict: StateFlow<DayPlanConflict?> = (application as CompanionApp).planSync.conflict

    /** While a request to the PC runs (start, end early). */
    val pcBusy = MutableStateFlow(false)

    /** Ending the PC's session early: the PC's terms while the dialog is open. */
    val endEarlyTerms = MutableStateFlow<EndEarlyTerms?>(null)
    private val now = MutableStateFlow(LocalDateTime.now())

    /** Runs only while the app is on screen; the notification and widget have their own refresh. */
    private var ticker: Job? = null

    val clock = ClockFormat(is24Hour = false) /* always 12-hour AM/PM (project convention) */
    private val builder = DayBarBuilder(clock)
    val state: StateFlow<CompanionState> = store.state

    /** Null while usage access isn't granted. */
    val phoneUsage = MutableStateFlow<PhoneUsage?>(null)

    val model: StateFlow<DayBarModel> = combine(store.state, now, phoneUsage) { state, time, _ -> build(state, time) }
        .stateIn(viewModelScope, SharingStarted.Eagerly, build(store.state.value, now.value))

    /** A message after an import or another action, shown once. */
    val message = MutableStateFlow<String?>(null)

    init {
        // What the watch says back (e.g. how pairing with the PC went) shows once, like other messages.
        viewModelScope.launch {
            watchBridge.news.filterNotNull().collect { news ->
                message.value = news
                watchBridge.clearNews()
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
        link.connect()
        ticker?.cancel()
        ticker = viewModelScope.launch {
            var ticks = 0
            while (true) {
                delay(30_000)
                now.value = LocalDateTime.now()
                if (++ticks % 10 == 0) {
                    refreshPhoneUsage()
                }
            }
        }
    }

    fun onStop() {
        (getApplication<CompanionApp>()).appOnScreen = false
        ticker?.cancel()
        ticker = null
        link.disconnect()
    }

    fun offerPairing(offer: PairingLink) {
        pairingOffer.value = offer
    }

    /** A pairing link pasted as text (when the camera only shows it). False when it isn't one. */
    fun pastePairingLink(text: String): Boolean {
        val offer = PairingLink.parse(text) ?: return false
        offerPairing(offer)
        return true
    }

    fun cancelPairing() {
        pairingOffer.value = null
    }

    fun confirmPairing() {
        val offer = pairingOffer.value ?: return
        viewModelScope.launch {
            pairing.value = true
            val error = link.pair(offer)
            pairing.value = false
            pairingOffer.value = null
            message.value = error ?: "Linked to ${offer.pcName}."
        }
    }

    /** The PC's link code for device sync was scanned (ADR 22): asks before joining. */
    fun offerJoin(offer: JoinLink) {
        joinOffer.value = offer
    }

    fun cancelJoin() {
        joinOffer.value = null
    }

    /** Joins the group; when the PC is connected, this phone's file goes to it at once. */
    fun confirmJoin() {
        val offer = joinOffer.value ?: return
        joinOffer.value = null
        if (!sync.join(offer.code)) {
            message.value = "That link code didn't work. Show it again on ${offer.pcName}."
            return
        }

        message.value = "Joined ${offer.pcName}'s devices."
        link.pairedPc.value?.let { pc -> viewModelScope.launch { link.sendSyncFiles(sync.filesForPc(pc.pcName)) } }
    }

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

    /** The running session's checklist (ADR 24): from the linked PC's state, else the newest sync file's open session. */
    val runningTasks: StateFlow<com.accountabilibuddy.companion.sync.RunningTasks?> =
        combine(link.status, link.lastState, sync.received) { status, last, received ->
            com.accountabilibuddy.companion.sync.RunningTasks.of(
                link.pairedPc.value?.pcName, (status as? LinkStatus.Connected)?.state ?: last?.state, received, java.time.Instant.now(),
            )
        }.stateIn(viewModelScope, SharingStarted.Eagerly, null)

    /** The PC's open come-backs (ADR 30), from the link or the newest received file; shown read-only on Today. */
    val comebacks: StateFlow<com.accountabilibuddy.companion.sync.OpenComebacks?> =
        combine(link.status, link.lastState, sync.received) { status, last, received ->
            com.accountabilibuddy.companion.sync.OpenComebacks.of(
                link.pairedPc.value?.pcName, (status as? LinkStatus.Connected)?.state ?: last?.state, received, java.time.Instant.now(),
            )
        }.stateIn(viewModelScope, SharingStarted.Eagerly, null)

    /** The Devices tab (ADR 22): the other devices' parts, the group's key and the phone's method switches. */
    val syncReceived = sync.received
    val syncKey = sync.key
    val syncSettings = sync.settings

    fun syncLinkCode(): String? = sync.linkCode()

    fun ownQrLinks(): List<String> = sync.ownQrLinks()

    fun saveSyncSettings(settings: PhoneSyncSettings) = sync.saveSettings(settings)

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

        sync.saveSettings(sync.settings.value.copy(folderOn = true, folderUri = uri.toString()))
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

    /** Google Drive's last result (ADR 26). */
    val driveStatus = driveSource.status

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
        if (driveSignIn.tokenFrom(data) != null) turnDriveOn() else message.value = "Google Drive wasn't allowed."
    }

    private fun turnDriveOn() {
        sync.saveSettings(sync.settings.value.copy(driveOn = true))
        message.value = "Signed in: this phone syncs through Google Drive."
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

    /** Joins the group from a typed link code; when the PC is connected, this phone's file goes to it at once. */
    fun joinWithCode(code: String): Boolean {
        if (!sync.join(code.trim())) {
            return false
        }

        message.value = "Joined your devices."
        link.pairedPc.value?.let { pc -> viewModelScope.launch { link.sendSyncFiles(sync.filesForPc(pc.pcName)) } }
        return true
    }

    fun leaveSyncGroup() {
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

    /** "Pair my watch instead": the scanned PC code goes to the linked watch, which pairs over Wi-Fi (ADR 21). */
    fun pairWatchWithPc() {
        val offer = pairingOffer.value ?: return
        pairingOffer.value = null
        viewModelScope.launch {
            message.value = watchBridge.sendPcOffer(PcOffer(offer.host, offer.port, offer.fingerprint, offer.code, offer.pcName))
        }
    }

    fun linkWatch(watch: WatchNode) {
        watchBridge.linkWatch(watch)
        message.value = "Linked ${watch.name}. Open AccountabiliBuddy on the watch."
    }

    fun unlinkWatch() = watchBridge.unlinkWatch()

    fun setWatchDataLayer(on: Boolean) = watchBridge.setDataLayer(on)

    fun setWatchRelay(on: Boolean) = watchBridge.setRelay(on)

    fun setWatchWifi(on: Boolean) = watchBridge.setWifiFallback(on)

    fun retryPc() {
        link.connect()
    }

    fun startOnPc(profileId: String?, minutes: Int?, countUp: Boolean) {
        viewModelScope.launch {
            pcBusy.value = true
            message.value = link.start(profileId, minutes, countUp) ?: "Started on the PC."
            pcBusy.value = false
        }
    }

    fun beginEndEarly() {
        viewModelScope.launch {
            pcBusy.value = true
            link.beginEndEarly()
                .onSuccess { endEarlyTerms.value = it }
                .onFailure { message.value = it.message }
            pcBusy.value = false
        }
    }

    fun cancelEndEarly() {
        endEarlyTerms.value = null
    }

    fun finishEndEarly(reason: String) {
        viewModelScope.launch {
            pcBusy.value = true
            val error = link.endEarly(reason)
            pcBusy.value = false
            if (error == null) {
                endEarlyTerms.value = null
                message.value = "The PC's session ended."
            } else {
                message.value = error
            }
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

    fun openPlanEditor(startTab: Int = 0): String? {
        planEditorTab = startTab
        val state = store.state.value
        val pending = state.pendingPlan
        val baseVersion = pending?.baseVersion ?: state.pcPlanVersion
            ?: return "Update AccountabiliBuddy on the PC to 0.1.48 or newer, then open this app while it's linked."
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
        DayPlanValidator.validate(draft.plan)?.let {
            message.value = it
            return false
        }

        store.savePendingPlan(draft.plan, Instant.now(), draft.baseVersion, draft.basePlan)
        planDraft.value = null
        message.value = if (linkStatus.value is LinkStatus.Connected) "Sending the plan to the PC…" else "Saved; it goes to the PC when they connect."
        return true
    }

    fun discardDayPlanEdit() {
        store.clearPendingPlan(null)
        message.value = "Edit discarded; showing the PC's plan."
    }

    fun useMyPlan() = (getApplication<CompanionApp>()).planSync.useMine()

    fun keepPcPlan() = (getApplication<CompanionApp>()).planSync.keepPc()

    fun unpairPc() {
        viewModelScope.launch {
            link.unpair()
            message.value = "Unpaired. The PC no longer knows this phone."
        }
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
            val focus = DayTimeline.planned(state.document.dayPlan, day).map { it.window }
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
        store.saveSleep(settings)
        refreshOutside()
    }

    fun setTimelineNotification(on: Boolean) {
        store.setTimelineNotification(on)
        refreshOutside()
    }

    fun setBedtimeReminder(on: Boolean) {
        store.setBedtimeReminder(on)
    }

    fun clearImport() {
        store.clearImport()
        message.value = "Imported data removed."
        refreshOutside()
    }

    fun onHealthPermissionResult() {
        checkHealth(readAfter = true)
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
        return builder.build(time, state.document, state.events, state.dayBar, state.sleep, state.slept, header, focus, (app.lunch.today()?.let { com.accountabilibuddy.companion.model.LocalWindow(java.time.LocalDateTime.ofInstant(it.start, java.time.ZoneId.systemDefault()), java.time.LocalDateTime.ofInstant(it.end, java.time.ZoneId.systemDefault())) }))
    }
}
