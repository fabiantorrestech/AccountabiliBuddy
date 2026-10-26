package com.accountabilibuddy.companion.notify

import android.Manifest
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.res.Configuration
import android.os.Build
import android.os.SystemClock
import android.view.View
import android.widget.RemoteViews
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.engine.DayBarModel
import com.accountabilibuddy.companion.model.SleepAmount
import com.accountabilibuddy.companion.ui.DayBarPainter

/**
 * The always-on, silent timeline card (like BridgeCal's): collapsed, the date, what's now or next and a small bar;
 * expanded, the full bar with hour labels and the sleep line. Colorized in the app's teal and held by
 * [PinnedTimelineService] so it stays at the top of the shade. The card's colour is fixed when it's posted, so the
 * bar is drawn once, for that colour; the pinned service posts the card again when the theme changes.
 */
object TimelineNotifier {
    // The noise button's width on the collapsed card.
    private const val NOISE_BUTTON_DP = 44

    const val ID = 1

    private const val CARD_NIGHT = 0xFF134E4A.toInt()
    private const val CARD_DAY = 0xFFCCFBF1.toInt()

    fun canPost(context: Context): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU ||
            context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED

    fun show(context: Context, model: DayBarModel) {
        if (!canPost(context)) {
            return
        }

        val night = (context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES
        val metrics = context.resources.displayMetrics
        val width = ((context.resources.configuration.screenWidthDp - SIDE_MARGINS_DP) * metrics.density).toInt()
        val text = if (night) 0xFFFFFFFF.toInt() else 0xFF042F2E.toInt()
        val subtle = if (night) 0xCCFFFFFF.toInt() else 0xB3042F2E.toInt()

        val noise = noiseButton(context)
        val collapsed = RemoteViews(context.packageName, R.layout.notification_timeline).apply {
            val counting = setCountdown(this, model, text)
            // The mode now first (the date is in the expanded card's small line).
            setTextViewText(R.id.timeline_title, if (counting) "" else model.statusCompact ?: model.statusText)
            setTextColor(R.id.timeline_title, text)
            // The bar is narrower by the noise button when it shows.
            setBar(this, model, if (noise != null) width - (NOISE_BUTTON_DP * metrics.density).toInt() else width, metrics.density, night, compact = true)
            showNoise(this, noise, text)
        }
        val expanded = RemoteViews(context.packageName, R.layout.notification_timeline_expanded).apply {
            val counting = setCountdown(this, model, text)
            // The biggest line says what's going on now (the countdown takes it while one runs); the date goes small.
            setTextViewText(R.id.timeline_title, if (counting) "" else model.statusText)
            setViewVisibility(R.id.timeline_title, if (counting) View.GONE else View.VISIBLE)
            setTextColor(R.id.timeline_title, text)
            showNoise(this, noise, text)
            val status = listOf(model.dateText, model.summaryText)
            setTextViewText(R.id.timeline_status, status.filter { it.isNotEmpty() }.joinToString(" · "))
            setTextColor(R.id.timeline_status, subtle)
            setBar(this, model, width, metrics.density, night, compact = false)
            val sleep = model.sleepLine
            if (sleep != null) {
                setTextViewText(R.id.timeline_sleep, sleep.text)
                setTextColor(R.id.timeline_sleep, sleepColor(sleep.amount, night))
                setViewVisibility(R.id.timeline_sleep, View.VISIBLE)
            } else {
                setViewVisibility(R.id.timeline_sleep, View.GONE)
            }
        }

        val open = PendingIntent.getActivity(
            context, 0, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )

        val notification = NotificationCompat.Builder(context, Channels.TIMELINE)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle(model.statusText)
            .setContentText(model.sleepLine?.text ?: model.summaryText)
            .setCustomContentView(collapsed)
            .setCustomBigContentView(expanded)
            .setStyle(NotificationCompat.DecoratedCustomViewStyle())
            .setColorized(com.accountabilibuddy.companion.ui.UiPrefs.barLook.value == com.accountabilibuddy.companion.ui.BarLook.Color)
            .setColor(if (night) CARD_NIGHT else CARD_DAY)
            .setOngoing(true)
            // Swiped away (Android 14+ allows it): posted again at once.
            .setDeleteIntent(RefreshReceiver.dismissedIntent(context, 100))
            .setSilent(true)
            .setOnlyAlertOnce(true)
            .setShowWhen(false)
            .setPriority(NotificationCompat.PRIORITY_MAX)
            .setCategory(NotificationCompat.CATEGORY_STATUS)
            // Its own group, so Android never bundles it with the bedtime reminder (that would unpin it).
            .setGroup(Channels.TIMELINE)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .setContentIntent(open)
            .build()

        try {
            NotificationManagerCompat.from(context).notify(ID, notification)
        } catch (e: SecurityException) {
            // Permission withdrawn meanwhile; nothing to show.
        }
    }

    fun hide(context: Context) {
        PinnedTimelineService.stop(context, removeNotification = true)
        NotificationManagerCompat.from(context).cancel(ID)
    }

    fun sleepColor(amount: SleepAmount, dark: Boolean): Int = when (amount) {
        SleepAmount.Good -> if (dark) 0xFF86EFAC.toInt() else 0xFF15803D.toInt()
        SleepAmount.Okay -> if (dark) 0xFFFDE68A.toInt() else 0xFFA16207.toInt()
        SleepAmount.Short -> if (dark) 0xFFFCA5A5.toInt() else 0xFFB91C1C.toInt()
    }

    /** The live countdown (a session's time left, a part's start) in the views' chronometer; false when nothing counts down. */
    // The noise player's play/pause: "▶" or "⏸" and what a tap does; null while no sound folder is chosen.
    private fun noiseButton(context: Context): Pair<String, android.app.PendingIntent>? {
        val player = (context.applicationContext as? com.accountabilibuddy.companion.CompanionApp)?.noise ?: return null
        if (player.settings.value.folderUri == null) {
            return null
        }

        val toggle = android.app.PendingIntent.getForegroundService(
            context, 41,
            android.content.Intent(context, com.accountabilibuddy.companion.noise.NoiseService::class.java)
                .setAction(com.accountabilibuddy.companion.noise.NoiseService.ACTION_TOGGLE),
            android.app.PendingIntent.FLAG_IMMUTABLE or android.app.PendingIntent.FLAG_UPDATE_CURRENT,
        )
        return (if (player.state.value.playing) "⏸\uFE0E" else "▶\uFE0E") to toggle
    }

    private fun showNoise(views: RemoteViews, noise: Pair<String, android.app.PendingIntent>?, color: Int) {
        if (noise == null) {
            views.setViewVisibility(R.id.timeline_noise, View.GONE)
            return
        }

        views.setViewVisibility(R.id.timeline_noise, View.VISIBLE)
        views.setTextViewText(R.id.timeline_noise, noise.first)
        views.setTextColor(R.id.timeline_noise, color)
        views.setOnClickPendingIntent(R.id.timeline_noise, noise.second)
    }

    private fun setCountdown(views: RemoteViews, model: DayBarModel, color: Int): Boolean {
        val end = model.countdownTo
        val format = model.countdownFormat
        val left = end?.let { java.time.Duration.between(java.time.Instant.now(), it).toMillis() }
        if (left == null || format == null || left <= 0) {
            views.setViewVisibility(R.id.timeline_countdown, View.GONE)
            return false
        }

        views.setChronometer(R.id.timeline_countdown, SystemClock.elapsedRealtime() + left, format, true)
        views.setChronometerCountDown(R.id.timeline_countdown, true)
        views.setTextColor(R.id.timeline_countdown, color)
        views.setViewVisibility(R.id.timeline_countdown, View.VISIBLE)
        return true
    }

    /** The bar in the colours of the card as posted ([night]: the dark teal card). */
    private fun setBar(views: RemoteViews, model: DayBarModel, width: Int, density: Float, night: Boolean, compact: Boolean) {
        val bar = DayBarPainter.bitmap(model, width, density, dark = night, compact = compact,
            onAccent = com.accountabilibuddy.companion.ui.UiPrefs.barLook.value == com.accountabilibuddy.companion.ui.BarLook.Color)
        views.setImageViewBitmap(R.id.timeline_bar, bar)
    }

    // The shade's card padding on both sides, roughly; the image scales to fit what's left.
    private const val SIDE_MARGINS_DP = 64
}
