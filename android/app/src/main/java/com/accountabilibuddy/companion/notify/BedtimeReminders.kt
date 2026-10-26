package com.accountabilibuddy.companion.notify

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.engine.BedtimeReminderTimes
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.model.BedtimeReminder
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Tonight's bedtime reminders on the phone (ADR 38): one exact alarm for the next one due (the phone's own and the
 * watch's), worked out again on every refresh so they follow bedtime when it moves; at each, a notification or the
 * full-screen alarm; from the first reminder until bedtime a live "Bedtime in …" notification. What fired, was
 * dismissed or snoozed tonight is kept in prefs "bedtimeReminders".
 */
object BedtimeReminders {
    private const val PREFS = "bedtimeReminders"
    private const val KEY_NIGHT = "night"
    private const val KEY_FIRED = "fired"
    private const val KEY_DISMISSED = "dismissed"
    private const val KEY_SNOOZE = "snooze"
    const val ACTION_FIRE = "com.accountabilibuddy.companion.BEDTIME_FIRE"
    const val ACTION_DISMISS = "com.accountabilibuddy.companion.BEDTIME_DISMISS"
    const val ACTION_SNOOZE = "com.accountabilibuddy.companion.BEDTIME_SNOOZE"
    const val EXTRA_NUMBER = "number"
    private const val COUNTDOWN_ID = 30
    private const val REMINDER_ID = 31
    const val ALARM_ID = 32
    val SNOOZE: Duration = Duration.ofMinutes(5)

    /** Tonight: its bedtime and the reminders for this phone or its watch; null past bedtime or with sleep off. */
    data class Tonight(val night: String, val bedtime: LocalDateTime, val due: List<BedtimeReminderTimes.Due>)

    fun tonight(context: Context, now: LocalDateTime = LocalDateTime.now()): Tonight? {
        val state = (context.applicationContext as CompanionApp).store.state.value
        if (!state.sleep.enabled) return null
        val night = state.sleep.toSchedule(state.document.dayPlan).nightAround(now)
        if (!night.end.isAfter(night.start) || !now.isBefore(night.start)) return null
        val due = BedtimeReminderTimes.forNight(night.start, state.sleep.reminders)
            .filter { BedtimeReminder.PHONE in it.reminder.devices || BedtimeReminder.WATCH in it.reminder.devices }
        return Tonight(BedtimeReminderTimes.nightId(night.start), night.start, due)
    }

    /** True while the countdown shows (the refresher then redraws every minute). */
    fun countdownActive(context: Context, now: LocalDateTime = LocalDateTime.now()): Boolean {
        val tonight = tonight(context, now) ?: return false
        val first = tonight.due.firstOrNull() ?: return false
        return !now.isBefore(first.at) && now.isBefore(tonight.bedtime)
    }

    /** Sets the alarm for the next reminder and shows or clears the countdown. Called on every refresh. */
    fun refresh(context: Context, clock: ClockFormat, now: LocalDateTime = LocalDateTime.now()) {
        val tonight = tonight(context, now)
        val prefs = prefs(context, tonight?.night)
        showCountdown(context, clock, tonight, now)
        val fired = prefs.getStringSet(KEY_FIRED, emptySet()).orEmpty()
        val snoozes = snoozes(context)
        // The next reminder not fired yet (a late one within the grace still counts), or a snooze.
        val next = tonight?.due?.firstOrNull { it.number.toString() !in fired && it.at.isAfter(now.minus(BedtimeReminderTimes.GRACE)) }
        val nextAt = listOfNotNull(next?.at, snoozes.values.minOrNull()?.let { LocalDateTime.ofInstant(it, ZoneId.systemDefault()) }).minOrNull()
        val manager = context.getSystemService(AlarmManager::class.java)
        val pending = firePending(context)
        if (nextAt == null) {
            manager.cancel(pending)
            return
        }

        val at = nextAt.atZone(ZoneId.systemDefault()).toInstant().toEpochMilli().coerceAtLeast(System.currentTimeMillis() + 1000)
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S || manager.canScheduleExactAlarms()) {
            manager.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pending)
        } else {
            manager.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pending)
        }
    }

    /** The alarm went off: fires what's due (a reminder or a snooze), then sets the next. */
    fun fire(context: Context, clock: ClockFormat, now: LocalDateTime = LocalDateTime.now()) {
        val tonight = tonight(context, now)
        val prefs = prefs(context, tonight?.night)
        val fired = prefs.getStringSet(KEY_FIRED, emptySet()).orEmpty().toMutableSet()
        val nowInstant = now.atZone(ZoneId.systemDefault()).toInstant()
        val snoozes = snoozes(context)
        snoozes.filterValues { !it.isAfter(nowInstant.plusSeconds(5)) }.keys.forEach { number ->
            clearSnooze(context, number)
            tonight?.due?.firstOrNull { it.number == number }?.let { show(context, clock, tonight, it, now) }
        }
        tonight?.due?.filter { it.number.toString() !in fired && !it.at.isAfter(now.plusSeconds(5)) && it.at.isAfter(now.minus(BedtimeReminderTimes.GRACE)) }
            ?.forEach { due ->
                fired += due.number.toString()
                show(context, clock, tonight, due, now)
            }
        prefs.edit().putStringSet(KEY_FIRED, fired).apply()
        refresh(context, clock, now)
    }

    /** Stops reminder [number] tonight on this phone (from here, the watch or a PC). */
    fun dismiss(context: Context, number: Int, fromHere: Boolean = true) {
        val tonight = tonight(context) ?: lastNight(context)
        clearSnooze(context, number)
        prefs(context, tonight?.night).let { it.edit().putStringSet(KEY_DISMISSED, it.getStringSet(KEY_DISMISSED, emptySet()).orEmpty() + number.toString()).apply() }
        NotificationManagerCompat.from(context).cancel(ALARM_ID)
        BedtimeAlarmActivity.stopIfShowing(number)
        if (fromHere) (context.applicationContext as CompanionApp).reminderRelay.send(tonight?.night, number, ReminderRelay.DISMISS)
    }

    /** Rings reminder [number] again in 5 minutes (here and, through the relay, on the other devices). */
    fun snooze(context: Context, number: Int, fromHere: Boolean = true, until: Instant = Instant.now().plus(SNOOZE)) {
        val tonight = tonight(context) ?: lastNight(context)
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putLong("$KEY_SNOOZE.$number", until.toEpochMilli()).apply()
        NotificationManagerCompat.from(context).cancel(ALARM_ID)
        BedtimeAlarmActivity.stopIfShowing(number)
        if (fromHere) (context.applicationContext as CompanionApp).reminderRelay.send(tonight?.night, number, ReminderRelay.SNOOZE, until)
        refresh(context, ClockFormat(is24Hour = false))
    }

    private fun show(context: Context, clock: ClockFormat, tonight: Tonight, due: BedtimeReminderTimes.Due, now: LocalDateTime) {
        val label = "Bedtime · ${due.number}/${due.count}"
        val left = Duration.between(now, tonight.bedtime).coerceAtLeast(Duration.ZERO)
        val text = if (left.toMinutes() <= 0) "It's bedtime (${clock.time(tonight.bedtime)})." else "Bedtime in ${DayBarBuilder.duration(left)} (${clock.time(tonight.bedtime)})."
        // The watch gets its own (it buzzes; an alarm buzzes until dismissed).
        if (BedtimeReminder.WATCH in due.reminder.devices) {
            (context.applicationContext as CompanionApp).reminderRelay.toWatch(tonight.night, due, label, text)
        }
        if (BedtimeReminder.PHONE !in due.reminder.devices || !TimelineNotifier.canPost(context)) return
        if (due.reminder.alarm) {
            BedtimeAlarmActivity.ring(context, due.number, label, text, due.reminder)
        } else {
            val notification = NotificationCompat.Builder(context, Channels.BEDTIME)
                .setSmallIcon(R.drawable.ic_stat_timeline)
                .setContentTitle(label)
                .setContentText(text)
                .setAutoCancel(true)
                .setContentIntent(openApp(context))
                .setColor(0xFF6366F1.toInt())
                .build()
            runCatching { NotificationManagerCompat.from(context).notify(REMINDER_ID + due.number, notification) }
        }
    }

    private fun showCountdown(context: Context, clock: ClockFormat, tonight: Tonight?, now: LocalDateTime) {
        val manager = NotificationManagerCompat.from(context)
        val first = tonight?.due?.firstOrNull()
        if (tonight == null || first == null || now.isBefore(first.at) || !TimelineNotifier.canPost(context)) {
            manager.cancel(COUNTDOWN_ID)
            return
        }

        val left = Duration.between(now, tonight.bedtime)
        val fired = prefs(context, tonight.night).getStringSet(KEY_FIRED, emptySet()).orEmpty()
        val next = tonight.due.firstOrNull { it.number.toString() !in fired && it.at.isAfter(now) }
        val notification = NotificationCompat.Builder(context, Channels.BEDTIME_COUNTDOWN)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle("Bedtime in ${DayBarBuilder.duration(left)} (${clock.time(tonight.bedtime)})")
            .setContentText(next?.let { "Next reminder ${it.number}/${it.count} at ${clock.time(it.at)}" } ?: "Wind down")
            .setOngoing(true)
            .setSilent(true)
            .setOnlyAlertOnce(true)
            .setContentIntent(openApp(context))
            .setColor(0xFF6366F1.toInt())
            .build()
        runCatching { manager.notify(COUNTDOWN_ID, notification) }
    }

    private fun snoozes(context: Context): Map<Int, Instant> {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        return prefs.all.filterKeys { it.startsWith("$KEY_SNOOZE.") }
            .mapNotNull { (key, value) -> (value as? Long)?.let { key.removePrefix("$KEY_SNOOZE.").toIntOrNull()?.to(Instant.ofEpochMilli(it)) } }
            .toMap()
    }

    private fun clearSnooze(context: Context, number: Int) =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().remove("$KEY_SNOOZE.$number").apply()

    // Tonight's record; a new night starts it over.
    private fun prefs(context: Context, night: String?): android.content.SharedPreferences {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        if (night != null && prefs.getString(KEY_NIGHT, null) != night) {
            prefs.edit().clear().putString(KEY_NIGHT, night).apply()
        }
        return prefs
    }

    private fun lastNight(context: Context): Tonight? =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(KEY_NIGHT, null)?.let { Tonight(it, LocalDateTime.now(), emptyList()) }

    private fun firePending(context: Context): PendingIntent = PendingIntent.getBroadcast(
        context, 40, Intent(context, BedtimeReminderReceiver::class.java).setAction(ACTION_FIRE),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    private fun openApp(context: Context): PendingIntent = PendingIntent.getActivity(
        context, 1, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )
}
