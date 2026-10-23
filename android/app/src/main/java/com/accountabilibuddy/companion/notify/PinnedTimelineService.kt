package com.accountabilibuddy.companion.notify

import android.app.Notification
import android.app.NotificationManager
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.content.res.Configuration
import android.os.Build
import android.os.IBinder
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.R
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

/**
 * Holds the timeline notification as its foreground notification, which is what lets Pixel keep a colorized
 * notification above conversations and ordinary notifications (the same approach as the user's BridgeCal app,
 * its ADR 3). It does no work of its own: [TimelineNotifier] builds and refreshes the card, and while this runs every
 * post with the timeline's id stays its foreground one. It also redraws when the screen comes on and when the
 * theme changes, so the now line and colours are current when the shade is opened.
 *
 * Android lets a foreground service start only from the foreground or a few exempt moments (boot, an app update),
 * so [tryStart] can fail; the card is then posted unpinned until the next chance.
 */
class PinnedTimelineService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private var lastNight = false

    private val screenOn = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            scope.launch { Refresher.refresh(applicationContext) }
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        isRunning = true
        lastNight = isNight(resources.configuration)
        // Unlocking too: a pickup during a session updates the parked notice and goes to the PC at once.
        val filter = IntentFilter(Intent.ACTION_SCREEN_ON).apply { addAction(Intent.ACTION_USER_PRESENT) }
        ContextCompat.registerReceiver(this, screenOn, filter, ContextCompat.RECEIVER_NOT_EXPORTED)
        // The sync folder at its chosen 1 or 5 minutes, which Android allows only while this service runs (ADR 22).
        scope.launch { (application as CompanionApp).folderSource.syncWhilePinned() }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        // startForeground is due within seconds; go foreground with what's already posted (or a placeholder) and
        // let the refresh post the real card.
        ServiceCompat.startForeground(this, TimelineNotifier.ID, postedCard() ?: placeholder(), foregroundType())
        scope.launch { Refresher.refresh(applicationContext) }
        return START_STICKY
    }

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        val night = isNight(newConfig)
        if (night != lastNight) {
            lastNight = night
            scope.launch { Refresher.refresh(applicationContext) }
        }
    }

    override fun onDestroy() {
        isRunning = false
        unregisterReceiver(screenOn)
        ServiceCompat.stopForeground(this, if (removeOnStop) ServiceCompat.STOP_FOREGROUND_REMOVE else ServiceCompat.STOP_FOREGROUND_DETACH)
        removeOnStop = false
        scope.cancel()
        super.onDestroy()
    }

    private fun postedCard(): Notification? =
        getSystemService(NotificationManager::class.java).activeNotifications.firstOrNull { it.id == TimelineNotifier.ID }?.notification

    private fun placeholder(): Notification =
        NotificationCompat.Builder(this, Channels.TIMELINE)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle(getString(R.string.app_name))
            .setOngoing(true)
            .setSilent(true)
            .build()

    private fun foregroundType(): Int =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE else 0

    private fun isNight(configuration: Configuration) =
        (configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES

    companion object {
        private const val TAG = "PinnedTimeline"

        @Volatile
        var isRunning = false
            private set

        @Volatile
        private var removeOnStop = false

        /** False when Android refuses a foreground-service start from the background right now. */
        fun tryStart(context: Context): Boolean =
            try {
                ContextCompat.startForegroundService(context, Intent(context, PinnedTimelineService::class.java))
                true
            } catch (e: IllegalStateException) {
                // ForegroundServiceStartNotAllowedException on Android 12+.
                Log.i(TAG, "Not allowed to pin from here; posting unpinned", e)
                false
            }

        /** Stops pinning; with [removeNotification] the card goes too (the notification was turned off). */
        fun stop(context: Context, removeNotification: Boolean) {
            if (!isRunning) {
                return
            }

            removeOnStop = removeNotification
            context.stopService(Intent(context, PinnedTimelineService::class.java))
        }
    }
}
