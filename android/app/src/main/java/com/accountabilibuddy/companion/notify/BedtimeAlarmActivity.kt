package com.accountabilibuddy.companion.notify

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.media.AudioAttributes
import android.media.MediaPlayer
import android.media.RingtoneManager
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.model.BedtimeReminder
import com.accountabilibuddy.companion.ui.CompanionTheme
import java.lang.ref.WeakReference

/**
 * A bedtime reminder that rings (ADR 38): the sound at its volume and the vibration until Dismiss or Snooze (5 min),
 * labelled "Bedtime · n/N". Opened through a full-screen notification (over the lock screen); the notification has
 * the same two buttons. Rings at most 10 minutes.
 */
class BedtimeAlarmActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setShowWhenLocked(true)
        setTurnScreenOn(true)
        showing = WeakReference(this)
        val number = intent.getIntExtra(BedtimeReminders.EXTRA_NUMBER, 0)
        val label = intent.getStringExtra(EXTRA_LABEL) ?: "Bedtime"
        val text = intent.getStringExtra(EXTRA_TEXT).orEmpty()
        setContent {
            CompanionTheme {
                Column(
                    Modifier.fillMaxSize().background(Color(0xFF0B1020)).padding(32.dp),
                    verticalArrangement = Arrangement.spacedBy(20.dp, Alignment.CenterVertically),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    Text(label, color = Color(0xFFA5B4FC), fontSize = 18.sp)
                    Text(text, color = Color.White, fontSize = 30.sp, textAlign = TextAlign.Center)
                    Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                        OutlinedButton(onClick = { BedtimeReminders.snooze(this@BedtimeAlarmActivity, number); finish() }) { Text("Snooze 5 min") }
                        Button(onClick = { BedtimeReminders.dismiss(this@BedtimeAlarmActivity, number); finish() }) { Text("Dismiss") }
                    }
                }
            }
        }
    }

    override fun onDestroy() {
        if (showing?.get() === this) showing = null
        super.onDestroy()
    }

    companion object {
        private const val EXTRA_LABEL = "label"
        private const val EXTRA_TEXT = "text"
        private const val MAX_RING_MS = 10 * 60_000L

        private var showing: WeakReference<BedtimeAlarmActivity>? = null
        private var player: MediaPlayer? = null
        private var vibrator: Vibrator? = null
        private var ringingNumber: Int? = null
        private val handler = Handler(Looper.getMainLooper())

        /** Starts ringing reminder [number]: sound, vibration and the full-screen notification. */
        fun ring(context: Context, number: Int, label: String, text: String, reminder: BedtimeReminder) {
            stop()
            ringingNumber = number
            val app = context.applicationContext
            val uri = RingtoneManager.getDefaultUri(
                when (reminder.sound) {
                    "chime" -> RingtoneManager.TYPE_NOTIFICATION
                    "rising" -> RingtoneManager.TYPE_RINGTONE
                    else -> RingtoneManager.TYPE_ALARM
                },
            ) ?: RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM)
            player = runCatching {
                MediaPlayer().apply {
                    setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_ALARM).setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build())
                    setDataSource(app, uri)
                    isLooping = true
                    val volume = reminder.volume.coerceIn(0, 100) / 100f
                    setVolume(volume, volume)
                    prepare()
                    start()
                }
            }.getOrNull()
            if (reminder.vibrate) {
                vibrator = app.getSystemService(Vibrator::class.java)?.also {
                    it.vibrate(VibrationEffect.createWaveform(longArrayOf(0, 600, 400), 0))
                }
            }
            handler.postDelayed({ if (ringingNumber == number) stop() }, MAX_RING_MS)

            val open = Intent(app, BedtimeAlarmActivity::class.java)
                .putExtra(BedtimeReminders.EXTRA_NUMBER, number).putExtra(EXTRA_LABEL, label).putExtra(EXTRA_TEXT, text)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
            val full = PendingIntent.getActivity(app, 50 + number, open, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            fun action(action: String, request: Int) = PendingIntent.getBroadcast(
                app, request, Intent(app, BedtimeReminderReceiver::class.java).setAction(action).putExtra(BedtimeReminders.EXTRA_NUMBER, number),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            val notification = NotificationCompat.Builder(app, Channels.BEDTIME_ALARM)
                .setSmallIcon(R.drawable.ic_stat_timeline)
                .setContentTitle(label)
                .setContentText(text)
                .setCategory(NotificationCompat.CATEGORY_ALARM)
                .setPriority(NotificationCompat.PRIORITY_MAX)
                .setOngoing(true)
                .setFullScreenIntent(full, true)
                .setContentIntent(full)
                .addAction(0, "Dismiss", action(BedtimeReminders.ACTION_DISMISS, 70 + number))
                .addAction(0, "Snooze 5 min", action(BedtimeReminders.ACTION_SNOOZE, 60 + number))
                .build()
            runCatching { NotificationManagerCompat.from(app).notify(BedtimeReminders.ALARM_ID, notification) }
        }

        /** Stops the ringing (and closes the screen) when it's reminder [number] (or any, for null). */
        fun stopIfShowing(number: Int?) {
            if (number == null || ringingNumber == null || ringingNumber == number) {
                stop()
                showing?.get()?.finish()
            }
        }

        private fun stop() {
            runCatching { player?.stop() }
            player?.release()
            player = null
            vibrator?.cancel()
            vibrator = null
            ringingNumber = null
        }
    }
}
