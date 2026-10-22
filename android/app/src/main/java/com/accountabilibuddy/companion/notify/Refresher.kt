package com.accountabilibuddy.companion.notify

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.PowerManager
import android.os.SystemClock
import android.text.format.DateFormat
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.widget.TimelineWidget
import androidx.glance.appwidget.updateAll
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.time.LocalDateTime

/**
 * Keeps the timeline notification, the widget and the bedtime reminder current: every few minutes (an inexact
 * alarm, so it costs almost no battery), after boot and clock changes, and right away when something changes.
 * While the screen is off and the card is pinned, only the reminder is checked: nobody sees the card or the widget,
 * and the pinned service redraws both when the screen comes on.
 */
object Refresher {
    private const val INTERVAL_MS = 5 * 60 * 1000L
    private const val COUNTDOWN_GRACE_MS = 5_000L

    // Screen-on, the alarm and the app can all ask at the same moment: one redraw at a time.
    private val drawing = Mutex()

    // When the header's countdown ends (a session, a part's start): the next redraw comes then if that's sooner.
    @Volatile
    private var countdownEnd: java.time.Instant? = null

    fun start(context: Context) {
        Channels.create(context)
        schedule(context)
    }

    /** Draws everything again now (call off the main thread for the widget part). */
    suspend fun refresh(context: Context) {
        drawing.withLock {
            val app = context.applicationContext as CompanionApp
            app.refreshWakeAlarm()
            val state = app.store.state.value
            val clock = ClockFormat(DateFormat.is24HourFormat(context))
            val now = LocalDateTime.now()

            if (isSeen(context)) {
                val builder = DayBarBuilder(clock)
                val focus = HeaderSource.focus(app, now)
                val header = HeaderSource.header(app, now, clock, HeaderSource.phoneMinutes(context, state, now, builder), focus)
                val model = builder.build(now, state.document, state.events, state.dayBar, state.sleep, state.slept, header, focus)
                countdownEnd = model.countdownTo
                if (state.timelineNotificationOn) {
                    TimelineNotifier.show(context, model)
                    if (!PinnedTimelineService.isRunning && TimelineNotifier.canPost(context)) {
                        // Fails quietly from the background; the card stays up unpinned until the next chance.
                        PinnedTimelineService.tryStart(context)
                    }
                } else {
                    TimelineNotifier.hide(context)
                }

                runCatching { TimelineWidget().updateAll(context) }
            }

            BedtimeReminder.check(context, app.store, clock, now)
            schedule(context)
        }
    }

    /** False while the screen is off and the card is pinned (the pinned service redraws when the screen comes on). */
    private fun isSeen(context: Context): Boolean =
        !PinnedTimelineService.isRunning || context.getSystemService(PowerManager::class.java).isInteractive

    private fun schedule(context: Context) {
        val alarms = context.getSystemService(AlarmManager::class.java)
        val intent = PendingIntent.getBroadcast(
            context, 0, Intent(context, RefreshReceiver::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val untilCountdownEnd = countdownEnd?.let { java.time.Duration.between(java.time.Instant.now(), it).toMillis() + COUNTDOWN_GRACE_MS }
        val delay = untilCountdownEnd?.takeIf { it in 1 until INTERVAL_MS } ?: INTERVAL_MS
        alarms.set(AlarmManager.ELAPSED_REALTIME, SystemClock.elapsedRealtime() + delay, intent)
    }
}
