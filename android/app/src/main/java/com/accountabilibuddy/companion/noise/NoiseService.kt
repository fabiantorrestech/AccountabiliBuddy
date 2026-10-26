package com.accountabilibuddy.companion.noise

import android.app.Notification
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.ServiceCompat
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.notify.Channels

/**
 * Keeps the noise player alive while it plays (a media-playback foreground service), with a quiet notification:
 * the sound's name, previous, play/pause, next. The widget's button and the notification's buttons come here.
 */
class NoiseService : Service() {
    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        running = true
        val noise = (application as CompanionApp).noise
        // Started as a foreground service (the widget's button, a play): go foreground before anything else.
        ServiceCompat.startForeground(this, ID, notification(noise), type())
        when (intent?.action) {
            ACTION_TOGGLE -> {
                val wasPlaying = noise.state.value.playing
                noise.toggle()
                // From the widget or a notification the bar isn't in sight: say why nothing plays.
                if (!wasPlaying && noise.lastPlayRefused) {
                    android.widget.Toast.makeText(this, "Connect headphones", android.widget.Toast.LENGTH_SHORT).show()
                }
            }
            ACTION_NEXT -> noise.next()
            ACTION_PREVIOUS -> noise.previous()
        }

        if (noise.state.value.playing) {
            getSystemService(android.app.NotificationManager::class.java).notify(ID, notification(noise))
        } else {
            ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
            stopSelf()
            running = false
        }
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        running = false
        super.onDestroy()
    }

    private fun notification(noise: NoisePlayer): Notification {
        val state = noise.state.value
        val builder = Notification.Builder(this, Channels.NOISE)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle(state.title ?: "Noise")
            .setContentText(if (state.playing) "Playing" else "Paused")
            .setOngoing(state.playing)
            .setShowWhen(false)
            .setContentIntent(PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE))
            .addAction(action("⏮", ACTION_PREVIOUS, 1))
            .addAction(action(if (state.playing) "Pause" else "Play", ACTION_TOGGLE, 2))
            .addAction(action("⏭", ACTION_NEXT, 3))
        val style = Notification.MediaStyle().setShowActionsInCompactView(0, 1, 2)
        noise.sessionToken()?.let { style.setMediaSession(it) }
        return builder.setStyle(style).build()
    }

    private fun action(label: String, action: String, request: Int): Notification.Action =
        Notification.Action.Builder(null, label, PendingIntent.getService(this, request, Intent(this, NoiseService::class.java).setAction(action),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)).build()

    private fun type(): Int =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK else 0

    companion object {
        const val ID = 4207
        const val ACTION_UPDATE = "com.accountabilibuddy.companion.noise.UPDATE"
        const val ACTION_TOGGLE = "com.accountabilibuddy.companion.noise.TOGGLE"
        const val ACTION_NEXT = "com.accountabilibuddy.companion.noise.NEXT"
        const val ACTION_PREVIOUS = "com.accountabilibuddy.companion.noise.PREVIOUS"

        @Volatile
        var running: Boolean = false
            private set
    }
}
