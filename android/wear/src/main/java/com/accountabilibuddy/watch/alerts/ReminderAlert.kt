package com.accountabilibuddy.watch.alerts

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.VibratorManager
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.shared.ReminderMessages
import com.accountabilibuddy.shared.WatchPaths
import com.accountabilibuddy.watch.R
import com.accountabilibuddy.watch.link.SnapshotStore
import com.google.android.gms.wearable.CapabilityClient
import com.google.android.gms.wearable.Wearable
import java.time.Instant

/**
 * A bedtime reminder on the watch (ADR 38), sent by the phone: a notification with a buzz; one that rings buzzes
 * every few seconds until Dismiss or Snooze (at most 5 minutes). Dismiss and Snooze go back to the phone, which
 * stops it on the other devices; one from the phone stops it here.
 */
object ReminderAlert {
    private const val CHANNEL = "bedtime"
    private const val ID = 7
    private const val MAX_BUZZ_MS = 5 * 60_000L
    const val ACTION_DISMISS = "com.accountabilibuddy.watch.REMINDER_DISMISS"
    const val ACTION_SNOOZE = "com.accountabilibuddy.watch.REMINDER_SNOOZE"
    private val handler = Handler(Looper.getMainLooper())
    private var buzzing: String? = null

    fun show(context: Context, ring: ReminderMessages.Ring) {
        val manager = context.getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel(CHANNEL, "Bedtime reminders", NotificationManager.IMPORTANCE_HIGH))
        fun action(action: String, request: Int) = PendingIntent.getBroadcast(
            context, request,
            Intent(context, ReminderActionReceiver::class.java).setAction(action).putExtra("night", ring.night).putExtra("number", ring.number),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val builder = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle(ring.label)
            .setContentText(ring.text)
            .setAutoCancel(!ring.alarm)
            .setCategory(if (ring.alarm) NotificationCompat.CATEGORY_ALARM else NotificationCompat.CATEGORY_REMINDER)
        if (ring.alarm) {
            builder.setOngoing(true)
                .addAction(0, "Snooze 5 min", action(ACTION_SNOOZE, 10 + ring.number))
                .addAction(0, "Dismiss", action(ACTION_DISMISS, 20 + ring.number))
        }
        runCatching { NotificationManagerCompat.from(context).notify(ID, builder.build()) }

        val vibrator = context.getSystemService(VibratorManager::class.java)?.defaultVibrator ?: return
        if (!ring.alarm) {
            vibrator.vibrate(VibrationEffect.createWaveform(longArrayOf(0, 200, 150, 200), -1))
            return
        }
        // Rings: a buzz every 3 seconds until dismissed, snoozed or 5 minutes.
        val key = "${ring.night}/${ring.number}"
        buzzing = key
        val started = System.currentTimeMillis()
        val buzz = object : Runnable {
            override fun run() {
                if (buzzing != key || System.currentTimeMillis() - started > MAX_BUZZ_MS) return
                vibrator.vibrate(VibrationEffect.createWaveform(longArrayOf(0, 500, 200, 500), -1))
                handler.postDelayed(this, 3_000)
            }
        }
        handler.post(buzz)
    }

    /** Stops reminder [number] of [night] here (dismissed or snoozed anywhere). */
    fun stop(context: Context, night: String, number: Int) {
        if (buzzing == "$night/$number" || buzzing == null) {
            buzzing = null
            NotificationManagerCompat.from(context).cancel(ID)
        }
    }

    /** Dismiss or Snooze on the watch: stops here and tells the phone. */
    fun act(context: Context, night: String, number: Int, action: String) {
        stop(context, night, number)
        val until = if (action == ReminderMessages.SNOOZE) Instant.now().plusSeconds(300) else null
        val bytes = ReminderMessages.write(ReminderMessages.Action(night, number, action, Instant.now(), until))
        val relayAllowed = SnapshotStore.snapshot(context).value?.relayAllowed ?: true
        Wearable.getCapabilityClient(context).getCapability(WatchPaths.PHONE_CAPABILITY, CapabilityClient.FILTER_REACHABLE)
            .addOnSuccessListener { info ->
                info.nodes.filter { relayAllowed || it.isNearby }.forEach { phone ->
                    Wearable.getMessageClient(context).sendMessage(phone.id, WatchPaths.REMINDER_ACTION, bytes)
                }
            }
    }
}

/** The reminder notification's Dismiss and Snooze on the watch. */
class ReminderActionReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val night = intent.getStringExtra("night") ?: return
        val number = intent.getIntExtra("number", 0)
        val action = if (intent.action == ReminderAlert.ACTION_SNOOZE) ReminderMessages.SNOOZE else ReminderMessages.DISMISS
        ReminderAlert.act(context, night, number, action)
    }
}
