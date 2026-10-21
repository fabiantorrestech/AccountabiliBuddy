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
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import com.accountabilibuddy.companion.link.EndEarlyTerms
import com.accountabilibuddy.companion.link.LinkStatus
import com.accountabilibuddy.companion.link.LinkedPc
import com.accountabilibuddy.companion.link.PairingLink
import com.accountabilibuddy.companion.engine.DayPlanValidator
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DayPlanConflict
import com.accountabilibuddy.companion.model.DayPlanDraft
import java.time.Instant
import java.time.LocalDateTime

/** The app's screens: the store's state, today's bar (redrawn every 30 seconds while on screen), importing and Health Connect. */
class CompanionViewModel(application: Application) : AndroidViewModel(application) {
    private val store = (application as CompanionApp).store
    private val sleepReader = SleepReader(application)
    private val usageReader = PhoneUsageReader(application)
    val curboxClient = CurboxClient(application)
    private val link = (application as CompanionApp).link

    /** The linked PC (ADR 14) and its session. */
    val linkStatus: StateFlow<LinkStatus> = link.status
    val pairedPc: StateFlow<LinkedPc?> = link.pairedPc
    val deviceName: String = (application as CompanionApp).deviceName

    /** A pairing link opened from the PC's QR code, waiting for the user to compare the code and confirm. */
    val pairingOffer = MutableStateFlow<PairingLink?>(null)
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

    val clock = ClockFormat(DateFormat.is24HourFormat(application))
    private val builder = DayBarBuilder(clock)
    val state: StateFlow<CompanionState> = store.state

    val model: StateFlow<DayBarModel> = combine(store.state, now) { state, time -> build(state, time) }
        .stateIn(viewModelScope, SharingStarted.Eagerly, build(store.state.value, now.value))

    /** A message after an import or another action, shown once. */
    val message = MutableStateFlow<String?>(null)
    val passwordRequest = MutableStateFlow<PasswordRequest?>(null)
    val importing = MutableStateFlow(false)
    val health = MutableStateFlow(HealthStatus.Checking)

    /** Null while usage access isn't granted. */
    val phoneUsage = MutableStateFlow<PhoneUsage?>(null)
    val curbox = MutableStateFlow(CurboxStatus())

    val healthPermissions: Set<String> get() = sleepReader.permissions

    /**
     * The app came on screen: catch up, then keep the bar's now line (every 30 s) and phone use (every 5 min)
     * current until [onStop]. Curbox is asked only when Settings shows it.
     */
    fun onStart() {
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
    fun openPlanEditor(): String? {
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

    private fun build(state: CompanionState, time: LocalDateTime): DayBarModel =
        builder.build(time, state.document, state.events, state.dayBar, state.sleep, state.slept)
}
