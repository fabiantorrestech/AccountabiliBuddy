package com.accountabilibuddy.companion.notify

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
        if (intent.action == Intent.ACTION_BOOT_COMPLETED && !StartOnBoot.isOn(context)) {
            return
        }

        val pending = goAsync()
        CoroutineScope(Dispatchers.Default).launch {
            try {
                Refresher.refresh(context.applicationContext)
            } finally {
                pending.finish()
            }
        }
    }
}
