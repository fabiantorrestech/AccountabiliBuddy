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

        val collapsed = RemoteViews(context.packageName, R.layout.notification_timeline).apply {
            val counting = setCountdown(this, model, text)
            setTextViewText(R.id.timeline_title, if (counting) "${model.dateText} · " else "${model.dateText} · ${model.statusText}")
            setTextColor(R.id.timeline_title, text)
            setBar(this, model, width, metrics.density, night, compact = true)
        }
        val expanded = RemoteViews(context.packageName, R.layout.notification_timeline_expanded).apply {
            setTextViewText(R.id.timeline_title, model.dateText)
            setTextColor(R.id.timeline_title, text)
            val counting = setCountdown(this, model, text)
            val status = if (counting) listOf(model.summaryText) else listOf(model.statusText, model.summaryText)
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
            .setContentTitle("${model.dateText} · ${model.statusText}")
            .setContentText(model.sleepLine?.text ?: model.summaryText)
            .setCustomContentView(collapsed)
            .setCustomBigContentView(expanded)
            .setStyle(NotificationCompat.DecoratedCustomViewStyle())
            .setColorized(true)
            .setColor(if (night) CARD_NIGHT else CARD_DAY)
            .setOngoing(true)
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
        val bar = DayBarPainter.bitmap(model, width, density, dark = night, compact = compact, onAccent = true)
        views.setImageViewBitmap(R.id.timeline_bar, bar)
    }

    // The shade's card padding on both sides, roughly; the image scales to fit what's left.
    private const val SIDE_MARGINS_DP = 64
}
