package com.accountabilibuddy.companion.notify

import android.app.PendingIntent
import com.accountabilibuddy.shared.StatusNotification
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

/**
 * The refresh alarm, boot, app update and clock changes: draw the notification and widget again. Also a swiped-away
 * timeline, parked or pre-work card ([dismissedIntent]): Android 14+ lets an ongoing notification be swiped, so it's
 * simply posted again (as MyCalendarWidget does).
 */
class RefreshReceiver : BroadcastReceiver() {
    companion object {
        private const val ACTION_TIMELINE_DISMISSED = "com.accountabilibuddy.companion.TIMELINE_DISMISSED"

        fun timelineDismissedIntent(context: Context): PendingIntent = PendingIntent.getBroadcast(
            context, 100,
            Intent(context, RefreshReceiver::class.java).setAction(ACTION_TIMELINE_DISMISSED),
            PendingIntent.FLAG_IMMUTABLE,
        )

        const val ACTION_DISMISSED = "com.accountabilibuddy.companion.NOTIFICATION_DISMISSED"

        /** Posted back at once when swiped away. */
        fun dismissedIntent(context: Context, requestCode: Int): android.app.PendingIntent =
            android.app.PendingIntent.getBroadcast(
                context, requestCode,
                Intent(context, RefreshReceiver::class.java).setAction(ACTION_DISMISSED),
                android.app.PendingIntent.FLAG_UPDATE_CURRENT or android.app.PendingIntent.FLAG_IMMUTABLE,
            )
    }

    override fun onReceive(context: Context, intent: Intent) {
        // Timers and alarms are set again after a reboot, an update or a clock change, whatever "Start when the phone
        // turns on" says (Android drops alarms at a reboot).
        if (intent.action == ACTION_TIMELINE_DISMISSED && !TimelineNotifier.restoreAfterSwipe(context)) return
        if (intent.action != ACTION_DISMISSED && intent.action != ACTION_TIMELINE_DISMISSED) {
            val clockChanged = intent.action == Intent.ACTION_TIME_CHANGED || intent.action == Intent.ACTION_TIMEZONE_CHANGED
            runCatching { com.accountabilibuddy.companion.timers.TimerScheduler.restore(context.applicationContext, clockChanged) }
        }
        if (intent.action == Intent.ACTION_BOOT_COMPLETED && !StartOnBoot.isOn(context)) {
            return
        }

        if (intent.action == Intent.ACTION_BOOT_COMPLETED || intent.action == Intent.ACTION_MY_PACKAGE_REPLACED) {
            StatusNotification.refresh(context)
        }
        val pending = goAsync()
        if (intent.action == Intent.ACTION_MY_PACKAGE_REPLACED) {
            // The app was updated: back up this phone's settings (when a folder is chosen).
            val app = context.applicationContext as com.accountabilibuddy.companion.CompanionApp
            if (app.autoBackup.folder != null) runCatching { app.autoBackup.backUpNow("app updated") }
        }
        CoroutineScope(Dispatchers.Default).launch {
            try {
                Refresher.refresh(context.applicationContext)
            } finally {
                pending.finish()
            }
        }
    }
}
