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
    // The header's text changes by the minute (the sleep left): redraw on the next minute.
    @Volatile
    private var minutely = false

    private const val INTERVAL_MS = 5 * 60 * 1000L
    private const val COUNTDOWN_GRACE_MS = 5_000L

    // Screen-on, the alarm and the app can all ask at the same moment: one redraw at a time.
    private val drawing = Mutex()

    // When the header's countdown ends (a session, a part's start): the next redraw comes then if that's sooner.
    @Volatile
    private var countdownEnd: java.time.Instant? = null

    // When the start nudge could come again: the next redraw comes then if that's sooner.
    @Volatile
    private var nudgeAt: java.time.Instant? = null
    // Tonight's bedtime, when a LockMyPhone-Extended profile is set for the night: the refresher runs then to start it.
    private var bedtimeAt: java.time.Instant? = null

    fun start(context: Context) {
        Channels.create(context)
        schedule(context)
    }

    /** Draws everything again now (call off the main thread for the widget part). */
    suspend fun refresh(context: Context) {
        drawing.withLock {
            val app = context.applicationContext as CompanionApp
            app.refreshWakeAlarm()
            runCatching { app.smartBedtime.refresh() }
            val state = app.store.state.value
            val clock = ClockFormat(is24Hour = false) /* always 12-hour AM/PM (project convention) */
            val now = LocalDateTime.now()

            if (isSeen(context)) {
                val builder = DayBarBuilder(clock)
                val focus = HeaderSource.focus(app, now)
                val header = HeaderSource.header(app, now, clock, HeaderSource.phoneMinutes(context, state, now, builder), focus)
                val model = builder.build(now, state.document, state.events, state.dayBar, state.sleep, state.slept, header, focus, (app.lunch.today()?.let { com.accountabilibuddy.companion.model.LocalWindow(java.time.LocalDateTime.ofInstant(it.start, java.time.ZoneId.systemDefault()), java.time.LocalDateTime.ofInstant(it.end, java.time.ZoneId.systemDefault())) }))
                countdownEnd = model.countdownTo
                minutely = header?.refreshMinutely == true || runCatching { BedtimeReminders.countdownActive(context) }.getOrDefault(false)
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

            // The master switch (and the car): while quiet, nothing nudges, parks or reminds.
            val quiet = app.pause.isQuiet
            com.accountabilibuddy.companion.pause.PauseReminder.check(context, app.pause)
            if (!quiet) {
                // ADR 38: tonight's reminders follow bedtime; the countdown redraws every minute.
                runCatching { BedtimeReminders.refresh(context, clock, now) }
            }
            // ADR 37: the day plan's block rules for Curbox (re-enable times, lift windows, heads-ups).
            runCatching { com.accountabilibuddy.companion.blocks.BlockGuard.run(context, now) }
            // Steering (start nudge, parked phone, pickups to the PC): from the devices' combined focus.
            val focus = HeaderSource.focus(app, now)?.takeUnless { quiet }
            // "Not working today" on the full-screen card, or lunch: no work nudges on the phone.
            val nudgeFocus = focus?.takeUnless {
                com.accountabilibuddy.companion.nudge.Takeover.isDayOff(context) || app.lunch.isOnLunch() || app.emergency.isActive()
            }
            // Lunch: "Lunch over!" once (its alarm may come late), and one hint when it isn't started by its time.
            com.accountabilibuddy.companion.lunch.LunchReceiver.tellOver(context)
            app.store.state.value.document.dayPlan.flexibleBreakOn(now.toLocalDate())?.let { flex ->
                val hint = flex.hintAt
                if (!quiet && hint != null && app.lunch.today() == null && !now.isBefore(hint) && now.isBefore(flex.window.end) && !app.lunch.hintedOn(now.toLocalDate())) {
                    app.lunch.markHinted(now.toLocalDate())
                    com.accountabilibuddy.companion.lunch.LunchReceiver.hint(context, clock.time(flex.window.end))
                }
            }
            WorkStartNudge.check(context, nudgeFocus)
            com.accountabilibuddy.companion.nudge.Takeover.maybeShow(context, nudgeFocus, clock)
            nudgeAt = WorkStartNudge.nextAt(context, nudgeFocus)
            val running = focus?.running
            // Hardcore: a session started on another device (or the phone's test session) asks LockMyPhone-Extended
            // for the chosen profile.
            val testRunning = !quiet && app.testSession.running() != null
            if (running != null || testRunning) com.accountabilibuddy.companion.essentials.EssentialsLaunch.maybeStartHardcore(context)
            // The night's lock: LockMyPhone-Extended's profile from bedtime until the wake time.
            com.accountabilibuddy.companion.essentials.EssentialsLaunch.maybeStartBedtimeHardcore(context)
            bedtimeAt = app.bedtimeEssentials.hardcoreProfile?.let {
                com.accountabilibuddy.companion.essentials.BedtimeNight.nextStart(app)?.atZone(java.time.ZoneId.systemDefault())?.toInstant()
            }
            // Curbox hard mode: a focus group that can't be exited, for each session on another device.
            runCatching { com.accountabilibuddy.companion.curbox.CurboxHardMode.check(context) }
            val pickups = running?.let { app.sync.unlocksSince(it.startedAt)?.size }
            ParkedNotice.check(context, running, pickups, clock)
            // Phase 11b: the placement detector follows the session (starts, breaks, ends).
            PinnedTimelineService.checkPlacement()
            val preWork = PreWorkNotice.part(focus, java.time.Instant.now())
            PreWorkNotice.check(context, preWork, preWork?.let { app.sync.unlocksSince(it.start)?.size }, clock)
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
        val untilNudge = nudgeAt?.let { java.time.Duration.between(java.time.Instant.now(), it).toMillis() + COUNTDOWN_GRACE_MS }
        val untilBedtime = bedtimeAt?.let { java.time.Duration.between(java.time.Instant.now(), it).toMillis() + COUNTDOWN_GRACE_MS }
        // The test session's end: the hard mode reactions (Curbox) are checked again then.
        val testEnd = (context.applicationContext as CompanionApp).testSession.running()?.second
        val untilTestEnd = testEnd?.let { java.time.Duration.between(java.time.Instant.now(), it).toMillis() + COUNTDOWN_GRACE_MS }
        // A work use ending: the parked notice reads "Done? Put it back" then.
        val untilWorkUse = (context.applicationContext as CompanionApp).workUse.activeUntil()
            ?.let { java.time.Duration.between(java.time.Instant.now(), it).toMillis() + COUNTDOWN_GRACE_MS }
        val untilMinute = if (minutely) 60_000L - System.currentTimeMillis() % 60_000L + 500L else null
        val delay = listOfNotNull(untilCountdownEnd, untilNudge, untilBedtime, untilTestEnd, untilWorkUse, untilMinute).filter { it in 1 until INTERVAL_MS }.minOrNull() ?: INTERVAL_MS
        alarms.set(AlarmManager.ELAPSED_REALTIME, SystemClock.elapsedRealtime() + delay, intent)
    }
}
