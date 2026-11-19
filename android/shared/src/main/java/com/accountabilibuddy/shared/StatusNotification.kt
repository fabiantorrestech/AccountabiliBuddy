package com.accountabilibuddy.shared

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/** A quiet, device-local shortcut that is available even when no feature card is showing. */
class StatusNotification : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == ACTION_SWIPED) refresh(context)
    }

    companion object {
        private const val CHANNEL = "app_status"
        private const val ID = 9000
        private const val PREFS = "status_notification"
        private const val ENABLED = "enabled"
        private const val ACTION_SWIPED = "com.accountabilibuddy.shared.STATUS_SWIPED"

        fun isEnabled(context: Context): Boolean =
            context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean(ENABLED, true)

        @Synchronized
        fun setEnabled(context: Context, enabled: Boolean) {
            context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putBoolean(ENABLED, enabled).apply()
            refresh(context)
        }

        @Synchronized
        fun refresh(context: Context) {
            val manager = context.getSystemService(NotificationManager::class.java)
            if (!isEnabled(context)) {
                manager.cancel(ID)
                return
            }
            if (!manager.areNotificationsEnabled()) return
            val launcher = context.packageManager.getLaunchIntentForPackage(context.packageName) ?: return
            val open = PendingIntent.getActivity(context, ID, launcher, PendingIntent.FLAG_IMMUTABLE)
            val swiped = PendingIntent.getBroadcast(
                context, ID, Intent(context, StatusNotification::class.java).setAction(ACTION_SWIPED),
                PendingIntent.FLAG_IMMUTABLE,
            )
            val channel = NotificationChannel(CHANNEL, "Persistent status notification", NotificationManager.IMPORTANCE_LOW)
            channel.setShowBadge(false)
            channel.setSound(null, null)
            manager.createNotificationChannel(channel)
            val notification = Notification.Builder(context, CHANNEL)
                .setSmallIcon(android.R.drawable.ic_dialog_info)
                .setContentTitle(context.applicationInfo.loadLabel(context.packageManager))
                .setContentText("Tap to open")
                .setContentIntent(open)
                .setDeleteIntent(swiped)
                .setCategory(Notification.CATEGORY_STATUS)
                .setOnlyAlertOnce(true)
                .setLocalOnly(true)
                .setShowWhen(false)
                .setOngoing(true)
                .build()
            try {
                manager.notify(ID, notification)
            } catch (_: SecurityException) {
                // Permission was withdrawn after the check.
            }
        }
    }
}
