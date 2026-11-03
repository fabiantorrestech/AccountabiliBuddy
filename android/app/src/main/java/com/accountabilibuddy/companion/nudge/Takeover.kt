package com.accountabilibuddy.companion.nudge

import android.app.KeyguardManager
import android.content.Context
import android.content.Intent
import android.graphics.PixelFormat
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.provider.AlarmClock
import android.provider.Settings
import android.telecom.TelecomManager
import android.util.Log
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.notify.HeaderSource
import com.accountabilibuddy.companion.notify.WorkStartNudge
import com.accountabilibuddy.companion.placement.PhoneDownLaunch
import com.accountabilibuddy.companion.sync.OpenComebacks
import com.accountabilibuddy.shared.WatchSnapshot
import com.accountabilibuddy.shared.WorkStart
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Shows the full-screen work nudge ([NudgeActivity]) when it's due and someone is looking: the phone unlocked and its
 * screen on, during a work stretch with no session. Android lets the app open it from the background only with
 * "Display over other apps", which the user turns on in Settings → AccountabiliBuddy on this phone.
 */
object Takeover {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private var returnWatch: Job? = null

    fun canShow(context: Context): Boolean = Settings.canDrawOverlays(context)

    /** The devices' combined focus as the card reads it; null while nothing may nudge (paused, a day off, lunch, emergency time). */
    fun nudgeFocus(app: CompanionApp, now: LocalDateTime = LocalDateTime.now()): WatchSnapshot? {
        if (app.pause.isQuiet || isDayOff(app) || app.lunch.isOnLunch() || app.emergency.isActive()) return null
        return HeaderSource.focus(app, now)
    }

    /**
     * The card was hidden without an answer: checks every 3 s, through [maybeShow] and all its conditions, and brings it back.
     * Stops once it's answered or back, when the screen goes off or locks, or while nothing may nudge; an unlock or the next
     * refresh shows it again then. An allowed app or a call in front keeps it waiting here.
     */
    fun watchUnanswered(context: Context) {
        val app = context.applicationContext as CompanionApp
        val store = NudgeStore(app)
        stopWatchingUnanswered()
        returnWatch = scope.launch {
            while (isActive) {
                delay(RETURN_CHECK_MS)
                if (app.nudgeOnScreen || !store.load().unanswered || !inUse(app)) return@launch
                val focus = nudgeFocus(app) ?: return@launch
                maybeShow(app, focus, ClockFormat(is24Hour = false))
            }
        }
    }

    // The app that came to the front last. The usage log only holds the moment an app came forward, so an app open for a long time
    // is found by looking back an hour the first time; after that only the time since the last read is queried (every 3 s while the
    // card waits) and the last answer stands until something else comes forward.
    private var frontCheckedAt: Instant? = null
    private var front: Pair<String, String?>? = null

    private fun appInFront(context: Context, now: Instant): Pair<String, String?>? {
        // Usage access taken away after a read: the cached answer is stale and nothing can refresh it.
        if (!com.accountabilibuddy.companion.usage.PhoneUsageReader(context).hasAccess()) {
            front = null
            frontCheckedAt = null
            return null
        }

        val sinceLast = frontCheckedAt?.let { Duration.between(it, now).seconds + 1 }
        val fresh = sinceLast == null || sinceLast < 0 || sinceLast > FRONT_LOOKBACK_SECONDS
        if (fresh) front = null
        val lookback = if (fresh) FRONT_LOOKBACK_SECONDS else sinceLast!!
        com.accountabilibuddy.companion.usage.PhoneUsageReader(context).foregroundApp(now, lookback)?.let { front = it }
        frontCheckedAt = now
        return front
    }

    // The phone's own dialer (a call that is not connected yet, so not "in call") and its clock (a ringing alarm or timer of
    // another app) in front: never covered, even when they are not on the Essentials list. Both lookups need no permission;
    // the clock's is found through the manifest's <queries> entry for SHOW_ALARMS, plus the known clock apps.
    private fun isDialerOrClock(context: Context, packageName: String): Boolean {
        if (packageName == context.getSystemService(TelecomManager::class.java)?.defaultDialerPackage) return true
        if (packageName in CLOCK_PACKAGES) return true
        return context.packageManager.resolveActivity(Intent(AlarmClock.ACTION_SHOW_ALARMS), 0)?.activityInfo?.packageName == packageName
    }

    fun stopWatchingUnanswered() {
        returnWatch?.cancel()
        returnWatch = null
    }

    /** @param focus the devices' combined focus; null while the phone is paused (then nothing shows). */
    fun maybeShow(context: Context, focus: WatchSnapshot?, clock: ClockFormat, now: Instant = Instant.now()) {
        if (focus == null || !canShow(context) || !inUse(context)) {
            Log.d(TAG, "not shown: focus=${focus != null} overlay=${canShow(context)} inUse=${inUse(context)}")
            return
        }

        val zone = ZoneId.systemDefault()
        val store = NudgeStore(context)
        val state = store.load()
        val stretch = WorkStart.current(focus, now)
        // No work stretch without a session (one started or the stretch ended): a card left behind is not owed any more.
        if (stretch == null && state.unanswered) store.save(state.copy(unanswered = false))
        val app = context.applicationContext as CompanionApp
        // Already up, or a ringing timer or alarm in front: never over those.
        if (app.nudgeOnScreen || app.ringOnScreen) return
        // Never over a call, ringing too.
        if (PhoneDownLaunch.inCall(context)) {
            Log.d(TAG, "not shown: in a call")
            return
        }
        // Emergency time, or unlocked into an always-allowed app (a call, a text, directions): no card.
        val inFront = appInFront(context, now)
        // Lunch (from any device) is a break: no card, as on the PC.
        if (app.emergency.isActive(now) || app.essentialsOnScreen || app.lunch.isOnLunch(now) || (inFront != null && (app.essentials.isAllowed(inFront.first, inFront.second) || isDialerOrClock(context, inFront.first)))) {
            Log.d(TAG, "not shown: emergency, lunch, allowed app, dialer or clock ($inFront)")
            return
        }

        val bonus = GoalBonus.of(focus, now, zone)
        val repeat = GoalBonus.repeatMinutes(focus, WorkStartNudge.repeatMinutes(context), now)
        if (!NudgeRules.due(state, stretch != null, stretch, now, LocalDate.now(zone), repeat)) {
            Log.d(TAG, "not due: inWork=${stretch != null} last=${state.lastShown}")
            return
        }

        store.save(state.copy(lastShown = now, unanswered = true))
        val until = focus.parts.filter { it.kind == "watch" && it.start <= now && now < it.end }.maxOfOrNull { it.end }
            ?.let { "Work until ${clock.time(LocalDateTime.ofInstant(it, zone))}" }
        val parked = OpenComebacks.of(app.sharedLists.parked.value, app.sync.received.value, now).items.map { it.text }
        val intent = Intent(context, NudgeActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
            .putExtra(NudgeActivity.EXTRA_UNTIL, until)
            .putStringArrayListExtra(NudgeActivity.EXTRA_PARKED, ArrayList(parked))
            .apply {
                if (bonus != null) {
                    putExtra(NudgeActivity.EXTRA_BONUS_FOCUSED, bonus.focusedMinutes)
                    putExtra(NudgeActivity.EXTRA_BONUS_GOAL, bonus.goalMinutes)
                    putExtra(NudgeActivity.EXTRA_BONUS_STREAK, bonus.streakDays)
                }
            }
        open(context, intent)
    }

    /**
     * Opens the card. Android lets an app open a screen from the background only while one of its windows is
     * showing, so with "Display over other apps" a tiny invisible window is put up for that moment, then removed.
     */
    /** Starts [intent]'s screen from the background (the essentials screen too), the same way. */
    fun startFromBackground(context: Context, intent: Intent) = open(context, intent)

    private fun open(context: Context, intent: Intent) {
        val app = context.applicationContext as CompanionApp
        Handler(Looper.getMainLooper()).post {
            if (app.appOnScreen) {
                start(context, intent)
                return@post
            }

            val windows = context.getSystemService(WindowManager::class.java)
            val view = View(context)
            val params = WindowManager.LayoutParams(
                1, 1, WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE,
                PixelFormat.TRANSLUCENT,
            ).apply { gravity = Gravity.TOP or Gravity.START }
            if (runCatching { windows.addView(view, params) }.isFailure) {
                start(context, intent)
                return@post
            }

            // Once it's on screen: open the card, then take the window away.
            view.postDelayed({
                start(context, intent)
                view.postDelayed({ runCatching { windows.removeView(view) } }, WINDOW_KEPT_MS)
            }, WINDOW_SETTLE_MS)
        }
    }

    private fun start(context: Context, intent: Intent) {
        runCatching { context.startActivity(intent) }.onFailure { Log.w(TAG, "work card not opened", it) }
    }

    private const val TAG = "Takeover"
    private const val RETURN_CHECK_MS = 3_000L
    private val CLOCK_PACKAGES = setOf("com.google.android.deskclock", "com.android.deskclock", "com.sec.android.app.clockpackage")
    private const val FRONT_LOOKBACK_SECONDS = 3_600L
    private const val WINDOW_SETTLE_MS = 150L
    private const val WINDOW_KEPT_MS = 1_500L

    /** True on a day the user said "Not working today" on the card (no phone nudges then). */
    fun isDayOff(context: Context): Boolean = NudgeStore(context).load().dayOff == LocalDate.now()

    // Screen on and unlocked: someone is using the phone.
    private fun inUse(context: Context): Boolean =
        context.getSystemService(PowerManager::class.java).isInteractive && !context.getSystemService(KeyguardManager::class.java).isKeyguardLocked
}
