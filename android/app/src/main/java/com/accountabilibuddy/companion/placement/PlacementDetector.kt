package com.accountabilibuddy.companion.placement

import android.app.KeyguardManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.hardware.TriggerEvent
import android.hardware.TriggerEventListener
import android.media.AudioManager
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.util.Log
import androidx.core.content.ContextCompat
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.EssentialsActivity
import com.accountabilibuddy.companion.essentials.EssentialsKind
import com.accountabilibuddy.companion.nudge.Takeover
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import java.time.Duration
import java.time.Instant

/**
 * Works out where the phone is during a PC focus session (planning included, never a break) from its own sensors, and
 * tells the PC through the sync file. Hosted by [com.accountabilibuddy.companion.notify.PinnedTimelineService], so
 * with the timeline notification off there is no detection. It never vibrates or sounds.
 *
 * Event-driven: screen on/off and unlock, the proximity sensor changing, and a wake-up significant-motion trigger each
 * ask for a look; on top of that a look about once a minute while the process runs. A look with the screen off is a
 * [PlacementSampler] burst; with the screen on it reads the screen and the app in front. While no session runs, or on
 * a break, nothing is registered and nothing is scheduled. No wake lock is held: with the CPU asleep the looks wait,
 * and a reading older than [PlacementStore.STALE_AFTER] reads as unknown, never as out.
 *
 * On the phone: 3 minutes of continuous in-use or face-up (no work use) open the essentials screen silently.
 * To the PC: on a change at most every 20 s, and a heartbeat about every 2 minutes, through the sync files and Drive.
 * Use from the main thread.
 */
class PlacementDetector(context: Context) {
    private val context = context.applicationContext
    private val app = this.context as CompanionApp
    private val handler = Handler(Looper.getMainLooper())
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val sensors = this.context.getSystemService(SensorManager::class.java)
    // The wake-up variant when the phone has one (a covered sensor must wake the CPU), else the ordinary one.
    private val proximity: Sensor? = sensors.getDefaultSensor(Sensor.TYPE_PROXIMITY, true) ?: sensors.getDefaultSensor(Sensor.TYPE_PROXIMITY)
    private val significantMotion: Sensor? = sensors.getDefaultSensor(Sensor.TYPE_SIGNIFICANT_MOTION)
    private val sampler = PlacementSampler(this.context, handler)
    private val power = this.context.getSystemService(PowerManager::class.java)
    private val keyguard = this.context.getSystemService(KeyguardManager::class.java)
    private val audio = this.context.getSystemService(AudioManager::class.java)
    private val usage = PhoneUsageReader(this.context)

    // Registered while a session runs and isn't on a break.
    private var sensing = false
    private var motionArmed = false
    private var proximityNear: Boolean? = null
    private var lastBurstAt: Instant = Instant.MIN

    // The app in front, kept between looks (asked incrementally); reset when the screen goes off.
    private var front: Pair<String, String?>? = null
    private var frontCheckedAt: Instant? = null

    // The current stretch of the phone being out.
    private var stretchStart: Instant? = null
    private var escalated = false
    private var lastClassifiedAt: Instant? = null

    private var lastSyncAt: Instant = Instant.MIN
    private var syncScheduled = false

    private var lookScheduled = false
    private val lookRunnable = Runnable {
        lookScheduled = false
        look(force = true)
    }
    private val syncLater = Runnable {
        syncScheduled = false
        syncNow()
    }

    private val screenReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            var pending: PendingResult? = null
            if (intent.action == Intent.ACTION_SCREEN_OFF) {
                front = null
                frontCheckedAt = null
                // The screen went off while "in use" was stored: the PC must not keep believing it for minutes. The
                // broadcast is held open (goAsync) so the CPU stays awake until the file is sent.
                if (storedState() == PlacementState.InUse) {
                    pending = goAsync()
                }
            }
            pending?.let { pushUnknown(Instant.now()) { it.finish() } }
            // Screen off: the sensors need a moment to settle before the burst.
            request(if (intent.action == Intent.ACTION_SCREEN_OFF) SETTLE_MS else 0L)
        }
    }

    private val proximityListener = object : SensorEventListener {
        override fun onSensorChanged(event: SensorEvent) {
            val sensor = proximity ?: return
            val near = event.values[0] < minOf(NEAR_CM, sensor.maximumRange)
            proximityNear = near
            // Covered while "face up" was stored: it isn't face up any more.
            if (near && storedState() == PlacementState.FaceUp) pushUnknown(Instant.now())
            request(SETTLE_MS)
        }

        override fun onAccuracyChanged(sensor: Sensor, accuracy: Int) = Unit
    }

    private val motionListener = object : TriggerEventListener() {
        override fun onTrigger(event: TriggerEvent) {
            // One-shot: armed again at the next look.
            motionArmed = false
            // Moved while "face up" was stored: it isn't lying still any more.
            if (storedState() == PlacementState.FaceUp) pushUnknown(Instant.now())
            request(SETTLE_MS)
        }
    }

    /** Starts listening for the screen; call from the service's onCreate. */
    fun start() {
        val filter = IntentFilter(Intent.ACTION_SCREEN_ON).apply {
            addAction(Intent.ACTION_SCREEN_OFF)
            addAction(Intent.ACTION_USER_PRESENT)
        }
        // Exported, like the service's own screen receiver: SystemUI sends USER_PRESENT (protected broadcasts no app can send).
        ContextCompat.registerReceiver(context, screenReceiver, filter, ContextCompat.RECEIVER_EXPORTED)
        look(force = true)
    }

    /** Stops everything; call from the service's onDestroy. Marks the detector as not running in the store. */
    fun stop() {
        runCatching { context.unregisterReceiver(screenReceiver) }
        stopSensing()
        handler.removeCallbacksAndMessages(null)
        app.placementStore.pause(Instant.now())
        // The service is going away: tell the PC "unknown" now (these calls run in the app's own scope, so cancelling ours is fine).
        app.syncPlacementNow()
        scope.cancel()
    }

    /** Something about the session may have changed (a sync file, a break, the test session): look once, without a burst storm. */
    fun poke() {
        handler.post { look(force = false) }
    }

    // The stored state while the detector is active; null otherwise.
    private fun storedState(): PlacementState? = app.placementStore.load()?.takeIf { it.active }?.state

    // The phone's stored state can no longer be believed: unknown now, and to the PC at once (no 20 s wait).
    private fun pushUnknown(now: Instant, done: (() -> Unit)? = null) {
        app.placementStore.reading(PlacementState.Unknown, now)
        resetStretch()
        syncNow(done)
    }

    private fun request(delayMs: Long) {
        handler.removeCallbacks(lookRunnable)
        lookScheduled = true
        handler.postDelayed(lookRunnable, delayMs)
    }

    private fun look(force: Boolean) {
        val now = Instant.now()
        val session = PlacementSession.current(app, now)
        when {
            session == null -> {
                stopSensing()
                val before = app.placementStore.load()
                app.placementStore.end(now)
                if (before != null && before.endedAt == null) syncSoon(now)
                resetStretch()
                return
            }

            session.onBreak || app.pause.isQuiet -> {
                stopSensing()
                val before = app.placementStore.load()
                app.placementStore.begin(session, now)
                app.placementStore.pause(now)
                if (before?.active == true) syncSoon(now)
                resetStretch()
                return
            }
        }

        app.placementStore.begin(session, now)
        startSensing()
        val interactive = power.isInteractive
        if (interactive) {
            sampler.cancel()
            classify(now, null, interactive)
            return
        }

        if (sampler.isRunning) {
            return
        }

        val gap = Duration.between(lastBurstAt, now)
        if (gap < (if (force) MIN_FORCED_GAP else MIN_GAP)) {
            keepTicking()
            return
        }

        lastBurstAt = now
        if (!sampler.isSupported || proximity == null) {
            classify(now, null, interactive)
            return
        }

        sampler.burst { sample ->
            val at = Instant.now()
            // The screen came on during the burst: it says nothing about the phone lying still.
            if (power.isInteractive) request(0L) else classify(at, sample, false)
        }
    }

    private fun classify(now: Instant, sample: PlacementSampler.Sample?, interactive: Boolean) {
        val unlocked = !keyguard.isKeyguardLocked
        val reading = PlacementReading(
            screenInteractive = interactive,
            unlocked = unlocked,
            essentialInFront = interactive && unlocked && essentialInFront(now),
            inCallOrEmergency = inCall() || app.emergency.isActive(now),
            proximityNear = proximityNear,
            lux = sample?.lux,
            gravityX = sample?.x,
            gravityY = sample?.y,
            gravityZ = sample?.z,
            variance = sample?.variance,
        )
        apply(PlacementClassifier.classify(reading), now)
        keepTicking()
    }

    private fun apply(state: PlacementState, now: Instant) {
        val store = app.placementStore
        val before = store.load()?.state
        store.reading(state, now)
        // No claim of "continuous" across a gap in the readings.
        if (lastClassifiedAt?.let { Duration.between(it, now) > PlacementStore.STALE_AFTER } == true) {
            resetStretch()
        }

        lastClassifiedAt = now
        val workUse = app.workUse.active(now) != null
        if (state.isOut && !workUse) {
            val started = stretchStart
            if (started == null) {
                stretchStart = now
                escalated = false
                store.addOut()
            } else if (!escalated && Duration.between(started, now) >= ESCALATE_AFTER) {
                escalated = escalate(now)
            }
        } else {
            resetStretch()
        }

        if (before != state) {
            syncSoon(now)
        } else if (Duration.between(lastSyncAt, now) >= HEARTBEAT) {
            syncNow()
        }
    }

    private fun resetStretch() {
        stretchStart = null
        escalated = false
        lastClassifiedAt = null
    }

    // Opens the essentials screen silently, with the skip rules of the essentials screen: locked, a call, an emergency.
    private fun escalate(now: Instant): Boolean {
        if (!power.isInteractive || keyguard.isKeyguardLocked || inCall() || app.emergency.isActive(now) || app.essentialsOnScreen) {
            return false
        }

        if (!app.essentials.onUnlockInSession) {
            return false
        }

        Log.i(TAG, "out for 3 minutes: opening the essentials screen")
        Takeover.startFromBackground(context, EssentialsActivity.intent(context, EssentialsKind.Focus))
        return true
    }

    private fun inCall(): Boolean = audio.mode.let {
        it == AudioManager.MODE_IN_CALL || it == AudioManager.MODE_IN_COMMUNICATION || it == AudioManager.MODE_RINGTONE
    }

    // Without Usage access nothing is known about the app in front, so it counts as not essential.
    private fun essentialInFront(now: Instant): Boolean {
        if (app.essentialsOnScreen) {
            return true
        }

        if (app.appOnScreen || !usage.hasAccess()) {
            return false
        }

        val since = frontCheckedAt?.let { Duration.between(it, now).seconds + 1 } ?: FIRST_FRONT_LOOKBACK_SECONDS
        usage.foregroundApp(now, since)?.let { front = it }
        frontCheckedAt = now
        return front?.let { (pkg, screen) -> app.essentials.isAllowed(pkg, screen) } ?: false
    }

    private fun startSensing() {
        if (!sensing) {
            sensing = true
            proximityNear = null
            proximity?.let { sensors.registerListener(proximityListener, it, SensorManager.SENSOR_DELAY_NORMAL, handler) }
        }

        if (!motionArmed && significantMotion != null) {
            motionArmed = sensors.requestTriggerSensor(motionListener, significantMotion)
        }
    }

    private fun stopSensing() {
        handler.removeCallbacks(lookRunnable)
        lookScheduled = false
        sampler.cancel()
        if (sensing) {
            sensing = false
            sensors.unregisterListener(proximityListener)
        }

        if (motionArmed) {
            significantMotion?.let { sensors.cancelTriggerSensor(motionListener, it) }
            motionArmed = false
        }

        lastBurstAt = Instant.MIN
        front = null
        frontCheckedAt = null
    }

    // The next look in about a minute, unless an event asks sooner.
    private fun keepTicking() {
        if (!lookScheduled) {
            request(TICK_MS)
        }
    }

    // A change: to the PC at once, but at most every 20 s (the latest change goes out when the wait is over).
    private fun syncSoon(now: Instant) {
        val wait = MIN_SYNC_GAP.minus(Duration.between(lastSyncAt, now))
        if (wait.isNegative || wait.isZero) {
            syncNow()
        } else if (!syncScheduled) {
            syncScheduled = true
            handler.postDelayed(syncLater, wait.toMillis())
        }
    }

    // The phone's file with the placement goes to the PCs through Drive.
    private fun syncNow(done: (() -> Unit)? = null) {
        lastSyncAt = Instant.now()
        scope.launch {
            try {
                runCatching {
                    app.driveSource.syncNow()
                }.onFailure { Log.w(TAG, "placement not synced", it) }
            } finally {
                done?.invoke()
            }
        }
    }

    private companion object {
        const val TAG = "Placement"
        const val TICK_MS = 60_000L
        const val SETTLE_MS = 1_000L
        const val NEAR_CM = 5f
        const val FIRST_FRONT_LOOKBACK_SECONDS = 600L
        val ESCALATE_AFTER: Duration = Duration.ofMinutes(3)
        val MIN_SYNC_GAP: Duration = Duration.ofSeconds(20)

        // About every 2 minutes (looks come a minute apart): the PC treats a placement older than 5 minutes as unknown.
        val HEARTBEAT: Duration = Duration.ofSeconds(100)

        // Bursts are at least this far apart: events (proximity flapping) 5 s, pokes from refreshes 20 s.
        val MIN_FORCED_GAP: Duration = Duration.ofSeconds(5)
        val MIN_GAP: Duration = Duration.ofSeconds(20)
    }
}
