package com.accountabilibuddy.companion.placement

import android.content.Context
import android.content.Intent
import android.media.AudioManager
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
import com.accountabilibuddy.companion.nudge.Takeover
import java.time.Instant

/**
 * When the "Phone down" screen ([PhoneDownActivity]) replaces the essentials screen: the phone is unlocked during a
 * real PC focus session (not the phone's test session) with the placement guard on. Never during a break, lunch, a
 * work use or emergency phone time, and never over a ringing or active call (incoming calls are whitelisted: the user
 * does nothing).
 */
object PhoneDownLaunch {
    /** True when the screen belongs in front right now: the conditions above, apart from the unlock itself. */
    fun applies(context: Context, now: Instant): Boolean {
        val app = context.applicationContext as CompanionApp
        return app.placementGuard.isOn(now) &&
            EssentialsLaunch.realSessionStart(app, now) != null &&
            !EssentialsLaunch.sessionOnBreak(app, now) &&
            !app.lunch.isOnLunch(now) &&
            app.workUse.active(now) == null &&
            !app.emergency.isActive(now) &&
            !inCall(context)
    }

    /** True when an unlock now should show the screen: [applies], the essentials-on-unlock switch is on, and it isn't up already. */
    fun shouldShowOnUnlock(context: Context, now: Instant): Boolean {
        val app = context.applicationContext as CompanionApp
        return app.essentials.onUnlockInSession && !app.phoneDownOnScreen && applies(context, now)
    }

    /** Shows the screen for an unlock at [now]; the unlock is logged so it counts as a pickup whatever the user opens next. */
    fun showOnUnlock(context: Context, now: Instant) {
        val app = context.applicationContext as CompanionApp
        app.phoneDown.recordShown(now)
        // The unlock wait applies when the placement read pocket just before the screen came on.
        val pocket = app.phoneDown.isOn(PocketPenalty.UnlockFriction, now) && app.placementStore.pocketRecently(now)
        Takeover.startFromBackground(context, intent(context, pocket))
    }

    /** The screen's intent; [pocket] starts it with the unlock wait. */
    fun intent(context: Context, pocket: Boolean = false): Intent =
        Intent(context, PhoneDownActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
            .putExtra(PhoneDownActivity.EXTRA_POCKET, pocket)

    // A call, ringing too.
    private fun inCall(context: Context): Boolean = context.getSystemService(AudioManager::class.java).mode.let {
        it == AudioManager.MODE_IN_CALL || it == AudioManager.MODE_IN_COMMUNICATION || it == AudioManager.MODE_RINGTONE
    }
}
