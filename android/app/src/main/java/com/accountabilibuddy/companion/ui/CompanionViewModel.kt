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
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
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

    fun cancelPcWaiting() = pcRemote.cancelWaiting()

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
        val currentGroup = sync.key.value?.groupId
        val linkGroup = SyncKeyCode.parse(link.code)?.groupId
        val settings = sync.settings.value
        joinOffer.value = JoinPrompt(
            link = link,
            leavesCurrentGroup = currentGroup != null && currentGroup != linkGroup,
            newRelay = link.relayUrl != null && !(settings.relayOn && settings.relayUrl == link.relayUrl),
            relayOnInternet = link.relayUrl != null && !RelayAddress.isLocalNetwork(link.relayUrl),
        )
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
        if (!sync.join(offer.code)) {
            message.value = "That link code didn't work. Show it again on ${offer.pcName}."
            return
        }

        // A link from a device that uses its own relay (ADR 46) sets this phone's relay up the same way.
        if (offer.relayUrl != null) {
            sync.saveSettings(
                sync.settings.value.copy(relayOn = true, relayUrl = offer.relayUrl, relayPin = offer.relayPin, relayAdmission = offer.relayAdmission),
            )
            message.value = "Joined ${offer.pcName}'s devices, through the relay."
            return
        }

        message.value = "Joined ${offer.pcName}'s devices."
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

    /** The running session's checklist (ADR 24): the newest sync file's open session. */
    val runningTasks: StateFlow<com.accountabilibuddy.companion.sync.RunningTasks?> =
        sync.received.map { received -> com.accountabilibuddy.companion.sync.RunningTasks.of(received, java.time.Instant.now()) }
            .stateIn(viewModelScope, SharingStarted.Eagerly, null)

    /** The PC's open come-backs (ADR 30), from the newest received file; shown read-only on Today. */
    val comebacks: StateFlow<com.accountabilibuddy.companion.sync.OpenComebacks?> =
        sync.received.map { received ->
            com.accountabilibuddy.companion.sync.OpenComebacks.of(received, java.time.Instant.now())
                ?: received.filter { it.file.device.kind == com.accountabilibuddy.shared.sync.SyncDeviceCard.KIND_PC }.maxByOrNull { it.file.device.writtenAt }?.file?.device?.name
                    ?.let { com.accountabilibuddy.companion.sync.OpenComebacks(it, emptyList()) }
        }.stateIn(viewModelScope, SharingStarted.Eagerly, null)

    /** The user's Beeminder goals (ADR 43) from the newest PC sync file that carries them; read-only on Today. */
    val beeminder: StateFlow<com.accountabilibuddy.companion.sync.BeeminderGoals?> =
        sync.received.map { received -> com.accountabilibuddy.companion.sync.BeeminderGoals.of(received, java.time.Instant.now()) }
            .stateIn(viewModelScope, SharingStarted.Eagerly, null)

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
        else message.value = "Google Drive wasn't allowed. Devices → Google Drive → \"How to set up Google Drive\" lists what the Google Cloud project needs."
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

    /** Joins the group from a typed link code; this phone's file goes out with the next Drive or folder sync. */
    fun joinWithCode(code: String): Boolean {
        val typed = code.trim()
        val target = SyncKeyCode.parse(typed) ?: return false

        // A different group than the one this phone is in: the same question and second confirmation as a join link.
        val currentGroup = sync.key.value?.groupId
        if (currentGroup != null && currentGroup != target.groupId) {
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

        message.value = "Joined your devices."
        return true
    }

    fun leaveSyncGroup() {
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

    fun beginEndEarly() {
        pcRemote.beginEndEarly()?.let { message.value = it }
    }

    fun cancelEndEarly() {
        endEarlyTerms.value = null
        pcRemote.cancelEndEarly()
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

    fun openPlanEditor(startTab: Int = 0): String? {
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
        DayPlanValidator.validate(draft.plan)?.let {
            message.value = it
            return false
        }

        store.savePendingPlan(draft.plan, Instant.now(), draft.baseVersion, draft.basePlan)
        planDraft.value = null
        message.value = when {
            pcRemote.target() != null -> "Saved; it goes to ${pcRemote.target()?.file?.device?.name} through the sync files."
            else -> "Saved; it goes to the PC once a PC file is here (Devices → Google Drive)."
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
     * "This week is A/B" on Today (ADR 47): moves week B's start so today's week is [weekB], saved like an Edit plan
     * change and sent to the PC, which decides (more work at once, less waits). Null when saved, else why not.
     */
    fun setThisWeek(weekB: Boolean): String? {
        openPlanEditor()?.let { return it }
        val plan = planDraft.value?.plan ?: return "The plan couldn't be opened."
        updatePlanDraft(com.accountabilibuddy.companion.engine.DayPlanEditing.withThisWeek(plan, weekB, java.time.LocalDate.now()))
        return if (savePlanDraft()) null else message.value ?: "Not saved."
    }

    fun discardDayPlanEdit() {
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
            val focus = DayWorkWindows.workWindows(state.document.dayPlan, day, LocalDateTime.now(), nightFallback = state.sleep::fallbackNightBefore).map { it.window }
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
        (getApplication<Application>() as com.accountabilibuddy.companion.CompanionApp).sleepSync.saveEdit(settings)
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
