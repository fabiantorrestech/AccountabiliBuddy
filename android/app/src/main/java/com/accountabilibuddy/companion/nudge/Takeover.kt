package com.accountabilibuddy.companion.nudge

import android.app.KeyguardManager
import android.content.Context
import android.content.Intent
import android.graphics.PixelFormat
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.provider.Settings
import android.util.Log
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.notify.WorkStartNudge
import com.accountabilibuddy.companion.sync.OpenComebacks
import com.accountabilibuddy.shared.WatchSnapshot
import com.accountabilibuddy.shared.WorkStart
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
    fun canShow(context: Context): Boolean = Settings.canDrawOverlays(context)

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
        val app = context.applicationContext as CompanionApp
        // Emergency time, or unlocked into an always-allowed app (a call, a text, directions): no card.
        val inFront = com.accountabilibuddy.companion.usage.PhoneUsageReader(context).foregroundApp(now)
        if (app.emergency.isActive(now) || app.essentialsOnScreen || (inFront != null && app.essentials.isAllowed(inFront.first, inFront.second))) {
            Log.d(TAG, "not shown: emergency or allowed app ($inFront)")
            return
        }

        val bonus = GoalBonus.of(focus, now, zone)
        val repeat = GoalBonus.repeatMinutes(focus, WorkStartNudge.repeatMinutes(context), now)
        if (!NudgeRules.due(state, stretch != null, stretch, now, LocalDate.now(zone), repeat)) {
            Log.d(TAG, "not due: inWork=${stretch != null} last=${state.lastShown}")
            return
        }

        store.save(state.copy(lastShown = now))
        val until = focus.parts.filter { it.kind == "watch" && it.start <= now && now < it.end }.maxOfOrNull { it.end }
            ?.let { "Work until ${clock.time(LocalDateTime.ofInstant(it, zone))}" }
        val parked = OpenComebacks.of(app.link.pairedPc.value?.pcName, app.link.lastState.value?.state, app.sync.received.value, now)
            ?.items?.map { it.text }.orEmpty()
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
    private const val WINDOW_SETTLE_MS = 150L
    private const val WINDOW_KEPT_MS = 1_500L

    /** True on a day the user said "Not working today" on the card (no phone nudges then). */
    fun isDayOff(context: Context): Boolean = NudgeStore(context).load().dayOff == LocalDate.now()

    // Screen on and unlocked: someone is using the phone.
    private fun inUse(context: Context): Boolean =
        context.getSystemService(PowerManager::class.java).isInteractive && !context.getSystemService(KeyguardManager::class.java).isKeyguardLocked
}
