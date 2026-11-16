package com.accountabilibuddy.companion.session

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import androidx.core.content.ContextCompat
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.companion.notify.Refresher
import com.accountabilibuddy.companion.sync.PcFileControl
import com.accountabilibuddy.shared.session.PhoneClockSource
import com.accountabilibuddy.shared.session.PhoneEndSettings
import com.accountabilibuddy.shared.session.PhoneSessionEndKind
import com.accountabilibuddy.shared.session.PhoneSessionMode
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.session.PhoneSessionSyncPart
import com.accountabilibuddy.shared.session.PhoneSessionSyncWriter
import com.accountabilibuddy.shared.sync.GroupSessions
import com.accountabilibuddy.shared.sync.ReceivedSyncFile
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.io.File
import java.time.Instant
import java.time.ZoneId
import java.util.UUID

/**
 * The phone session on this phone (ADR 85, PS2a): the store, the clock, the alarms and [PhoneSessionController] wired to the app. It starts
 * with the process ([start]), so a boot, an update or Android killing the app all end in the same place: the saved session is looked at, the
 * gap with the phone off is judged, the alarms are set again. No UI of its own.
 *
 * The parts other tasks fill in are hooks here, do nothing until they are set: [onSyncWrite] (PS2b: the app writes this phone's file now; the file's
 * session part is [syncPart]), [onEnforceUntil] and [onRelease] (PS2c: LockMyPhone, Curbox).
 */
class PhoneSessionRuntime(private val app: CompanionApp) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val alarm = PhoneSessionAlarm(app)
    private val _running = MutableStateFlow<PhoneSessionRecord?>(null)

    /** PS2b: write this phone's sync file now, by every way that is on (never called for a test session). */
    @Volatile
    var onSyncWrite: (() -> Unit)? = null

    /** PS2c: the enforcement is asked to hold until the time (null: no set end). */
    @Volatile
    var onEnforceUntil: ((PhoneSessionRecord, Instant?) -> Unit)? = null

    /** PS2c: the session ended, release what it asked for. */
    @Volatile
    var onRelease: ((PhoneSessionRecord, PhoneSessionEndKind) -> Unit)? = null

    private val ports = object : PhoneSessionPorts {
        override val store = PhoneSessionStore(
            File(app.filesDir, "session/${PhoneSessionStore.SESSIONS_FILE}"),
            File(app.filesDir, "session/${PhoneSessionStore.REASONS_FILE}"),
            KeystorePhoneSessionSealer(),
        )
        override val clockSource: PhoneClockSource = AndroidPhoneClockSource(app)

        override fun mode(): PhoneSessionMode =
            if (app.guidance.isCompanion) PhoneSessionMode.PRODUCTA_BUDDY else PhoneSessionMode.ACCOUNTABILI_BUDDY

        // The phone's own "Ending a phone session early" setting is PS3b's; until it writes these keys the defaults (60 s, 15 characters) stand.
        override fun endSettings(): PhoneEndSettings {
            val prefs = app.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            return PhoneEndSettings(
                prefs.getInt(KEY_WAIT_SECONDS, PhoneEndSettings.DEFAULT.waitSeconds).coerceAtLeast(0),
                prefs.getInt(KEY_MIN_REASON, PhoneEndSettings.DEFAULT.minReasonLength).coerceAtLeast(0),
            )
        }

        override fun zone(): ZoneId = ZoneId.systemDefault()

        override fun lunchRunning(): Boolean = app.lunch.isOnLunch(TrustedTime.now())

        override fun lunchStart(): Instant? = app.lunch.lunch.value?.start

        // R1: the group's rule, the same one the Today PC card reads (PcFileControl.groupLive for a PC's file).
        override fun liveElsewhere(now: Instant): Boolean {
            val others = app.sync.received.value.map { ReceivedSyncFile(it.file, it.receivedAt) }
            return GroupSessions.liveElsewhere(others, now) { PcFileControl.groupLive(it, now) } != null
        }

        override fun newId(): String = UUID.randomUUID().toString()

        override fun scheduleAlarm(kind: PhoneAlarmKind, afterMs: Long?) = alarm.schedule(kind, afterMs)

        override fun requestSyncWrite() {
            onSyncWrite?.invoke()
        }

        override fun enforceUntil(record: PhoneSessionRecord, endsAt: Instant?) {
            onEnforceUntil?.invoke(record, endsAt)
        }

        override fun releaseEnforcement(record: PhoneSessionRecord, kind: PhoneSessionEndKind) {
            onRelease?.invoke(record, kind)
        }

        override fun changed() {
            _running.value = controller.running
            scope.launch { runCatching { Refresher.refresh(app) } }
        }
    }

    /** The only writer of a phone session. */
    val controller: PhoneSessionController = PhoneSessionController(ports)

    /** The running session (a test one too), null when none; follows every change. */
    val running: StateFlow<PhoneSessionRecord?> = _running.asStateFlow()

    private val shutdown = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            runCatching { controller.onShutdown() }
        }
    }

    /** Called once from [CompanionApp.onCreate]: judges the saved session and listens for the phone going off. */
    fun start() {
        _running.value = controller.running
        runCatching { controller.onStart() }
        _running.value = controller.running
        // ACTION_SHUTDOWN can't be a manifest receiver on current Android: while the process lives (the pinned service keeps it) it hears it; a
        // process that was already gone leaves the last heartbeat (saved at every refresh) as the moment the phone went off.
        runCatching { ContextCompat.registerReceiver(app, shutdown, IntentFilter(Intent.ACTION_SHUTDOWN), ContextCompat.RECEIVER_NOT_EXPORTED) }
        // A lunch started or ended on any device (here, or from a received file) pauses or resumes the session's countdown.
        scope.launch { app.lunch.lunch.collect { runCatching { controller.observeLunch() } } }
    }

    /**
     * PS2b: the session part of this phone's sync file: `running`, the counted sessions as pieces, `coversFrom` and the file's own time, all from the session's
     * trusted clock (B4). [fallbackNow] is the phone's trusted time, used only when the session clock can't stamp a file yet (see [PhoneSessionSyncWriter]).
     */
    fun syncPart(fallbackNow: Instant): PhoneSessionSyncPart = PhoneSessionSyncWriter.forFile(controller.syncInput(), fallbackNow)

    /**
     * Troubleshoot's report lines about this phone's sessions (PS2b, Q1181): plain words, no times and no text. A phone session whose times could not be checked
     * before the network time arrived is not sent until it is corrected; one that can't be corrected any more stays on this phone.
     */
    fun troubleshootLines(): List<String> {
        val status = controller.sendStatus()
        return buildList {
            if (status.waiting) add("This phone's sessions wait for network time before they're sent.")
            if (status.lost) add("Some of this phone's sessions can't be sent: their time couldn't be checked. They stay on this phone.")
        }
    }

    /** PS2b: a real session (never a test one) is running: the sync passes run at their busy pace and the file is rewritten at least every 2 minutes (B13). */
    fun realSessionRunning(): Boolean = _running.value?.let { it.isRunning && !it.isTest } == true

    /** Every redraw of the notification: the network time may have arrived, a lunch may be over, the end may have come, the heartbeat is saved. */
    fun refresh() {
        controller.refresh()
        _running.value = controller.running
    }

    private companion object {
        const val PREFS = "phoneSession"
        const val KEY_WAIT_SECONDS = "waitSeconds"
        const val KEY_MIN_REASON = "minReason"
    }
}
