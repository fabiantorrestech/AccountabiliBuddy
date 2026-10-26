package com.accountabilibuddy.companion.timers

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.media.AudioAttributes
import android.media.MediaPlayer
import android.media.RingtoneManager
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.notify.Channels
import com.accountabilibuddy.companion.notify.TimelineNotifier
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update

/**
 * Timers that are up and alarms that ring, like a clock app (and `BedtimeAlarmActivity`): the alarm sound and the
 * vibration until each is dismissed, snoozed or given +1 min (at most 10 minutes), a full-screen notification per item
 * (over the lock screen, [TimerRingActivity]) with the same buttons. Several can ring at once; the sound stops with
 * the last.
 */
object TimerRinger {
    private const val MAX_RING_MS = 10 * 60_000L
    private const val COLOR = 0xFF0D9488.toInt()

    private val _ringing = MutableStateFlow<List<RingingItem>>(emptyList())

    /** What rings now, oldest first (the ring screen lists them). */
    val ringing: StateFlow<List<RingingItem>> = _ringing.asStateFlow()

    private var player: MediaPlayer? = null
    private var vibrator: Vibrator? = null
    private val handler = Handler(Looper.getMainLooper())
    private val stopSound = Runnable { stopSound() }

    fun ring(context: Context, item: RingingItem) {
        val app = context.applicationContext
        _ringing.update { list -> list.filterNot { it.isSame(item.kind, item.id) } + item }
        startSound(app)
        if (TimelineNotifier.canPost(app)) {
            runCatching { NotificationManagerCompat.from(app).notify(TimerNotifications.id(item.kind, item.id), notification(app, item)) }
        }
    }

    /**
     * Stops item [id] ringing and clears its notification (also one left from before the app was restarted; a timer's
     * countdown is posted again by the store's change that follows).
     */
    fun stop(context: Context, kind: RingingItem.Kind, id: Int) {
        _ringing.update { list -> list.filterNot { it.isSame(kind, id) } }
        NotificationManagerCompat.from(context).cancel(TimerNotifications.id(kind, id))
        // The same Runnable as the 10-minute stop, so a ring that starts right after cancels it.
        if (_ringing.value.isEmpty()) handler.post(stopSound)
    }

    @Synchronized
    private fun startSound(app: Context) {
        handler.removeCallbacks(stopSound)
        handler.postDelayed(stopSound, MAX_RING_MS)
        if (player != null) return
        val uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM) ?: RingtoneManager.getDefaultUri(RingtoneManager.TYPE_RINGTONE)
        player = runCatching {
            MediaPlayer().apply {
                setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_ALARM).setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build())
                setDataSource(app, uri)
                isLooping = true
                prepare()
                start()
            }
        }.getOrNull()
        vibrator = app.getSystemService(Vibrator::class.java)?.also {
            it.vibrate(VibrationEffect.createWaveform(longArrayOf(0, 600, 400), 0))
        }
    }

    @Synchronized
    private fun stopSound() {
        handler.removeCallbacks(stopSound)
        runCatching { player?.stop() }
        player?.release()
        player = null
        vibrator?.cancel()
        vibrator = null
    }

    private fun notification(app: Context, item: RingingItem): android.app.Notification {
        val open = Intent(app, TimerRingActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        val full = PendingIntent.getActivity(app, TimerNotifications.id(item.kind, item.id), open, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val builder = NotificationCompat.Builder(app, Channels.TIMER_RING)
            .setSmallIcon(if (item.kind == RingingItem.Kind.TIMER) R.drawable.ic_stat_timer else R.drawable.ic_stat_alarm)
            .setContentTitle(item.title)
            .setContentText(item.text)
            .setCategory(NotificationCompat.CATEGORY_ALARM)
            .setPriority(NotificationCompat.PRIORITY_MAX)
            .setOngoing(true)
            .setFullScreenIntent(full, true)
            .setContentIntent(full)
            .setColor(COLOR)
        if (item.kind == RingingItem.Kind.TIMER) {
            builder.addAction(0, "Dismiss", TimerNotifications.action(app, TimerReceiver.ACTION_DISMISS_TIMER, item.id, 5))
                .addAction(0, "+1 min", TimerNotifications.action(app, TimerReceiver.ACTION_ADD_MINUTE, item.id, 6))
        } else {
            builder.addAction(0, "Dismiss", TimerNotifications.action(app, TimerReceiver.ACTION_DISMISS_ALARM, item.id, 5))
                .addAction(0, "Snooze 9 min", TimerNotifications.action(app, TimerReceiver.ACTION_SNOOZE_ALARM, item.id, 7))
        }
        return builder.build()
    }
}
