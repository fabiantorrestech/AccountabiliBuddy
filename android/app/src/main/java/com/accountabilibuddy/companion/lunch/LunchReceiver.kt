package com.accountabilibuddy.companion.lunch

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.notify.Channels
import com.accountabilibuddy.companion.notify.TimelineNotifier

/** Lunch's end (its alarm, or a refresh that finds it over): "Lunch over!" once, popping over the app in use. */
class LunchReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        tellOver(context)
    }

    companion object {
        private const val OVER_ID = 9
        private const val HINT_ID = 10

        fun tellOver(context: Context) {
            val lunch = (context.applicationContext as? CompanionApp)?.lunch ?: return
            if (!lunch.overDue() || !TimelineNotifier.canPost(context)) return
            lunch.markOverTold()
            post(context, OVER_ID, "Lunch over!", "Welcome back. Plan your next task and start a session on your computer.")
        }

        fun hint(context: Context, until: String) {
            if (!TimelineNotifier.canPost(context)) return
            post(context, HINT_ID, "Time for lunch?", "Lunch isn't started yet. Take it before $until: Start lunch on Today.")
        }

        private fun post(context: Context, id: Int, title: String, text: String) {
            val open = PendingIntent.getActivity(
                context, id, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            val notification = NotificationCompat.Builder(context, Channels.WORK_NUDGE)
                .setSmallIcon(R.drawable.ic_stat_timeline)
                .setContentTitle(title)
                .setContentText(text)
                .setStyle(NotificationCompat.BigTextStyle())
                .setPriority(NotificationCompat.PRIORITY_HIGH)
                .setAutoCancel(true)
                .setContentIntent(open)
                .setColor(0xFFCA8A04.toInt())
                .build()
            try {
                NotificationManagerCompat.from(context).notify(id, notification)
            } catch (e: SecurityException) {
                // Permission withdrawn meanwhile.
            }
        }
    }
}
