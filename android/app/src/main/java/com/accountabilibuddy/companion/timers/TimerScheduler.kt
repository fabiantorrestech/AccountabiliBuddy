package com.accountabilibuddy.companion.timers

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import com.accountabilibuddy.companion.CompanionApp

/**
 * The timers' and alarms' wake-ups: one exact alarm at the soonest end or ring (the bedtime reminders' permission,
 * USE_EXACT_ALARM; `setExactAndAllowWhileIdle`, not `setAlarmClock`, so a timer never shows as the phone's next alarm
 * clock, which ADR 23 reads as the wake time), and while a timer runs a progress tick every 20 s that doesn't wake the
 * phone (the notifications' chronometers tick by themselves; only their bars wait for it).
 */
object TimerScheduler {
    const val ACTION_FIRE = "com.accountabilibuddy.companion.TIMER_FIRE"
    const val ACTION_TICK = "com.accountabilibuddy.companion.TIMER_TICK"
    private const val FIRE_REQUEST = 300_000
    private const val TICK_REQUEST = 300_001
    private const val TICK_MS = 20_000L
    private const val TICK_WINDOW_MS = 10_000L

    /** Sets the alarms for what's waiting and redraws the notifications. Called after every change. */
    fun apply(context: Context, store: TimersStore) {
        val manager = context.getSystemService(AlarmManager::class.java) ?: return
        val now = System.currentTimeMillis()
        val fire = pending(context, ACTION_FIRE, FIRE_REQUEST)
        val due = store.nextDueMs()
        if (due == null) {
            manager.cancel(fire)
        } else {
            // One missed while the phone was off is fired at once (rung or not, by how late it is).
            val at = due.coerceAtLeast(now + 1_000)
            if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S || manager.canScheduleExactAlarms()) {
                manager.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, fire)
            } else {
                manager.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, fire)
            }
        }

        val tick = pending(context, ACTION_TICK, TICK_REQUEST)
        if (store.anyRunning()) {
            // RTC, not RTC_WAKEUP: with the screen off it waits for the phone to wake for something else.
            manager.setWindow(AlarmManager.RTC, now + TICK_MS, TICK_WINDOW_MS, tick)
        } else {
            manager.cancel(tick)
        }
        TimerNotifications.update(context, store.timers.value, now)
    }

    /** The exact alarm went off: rings what's due, then sets the next. */
    fun fire(context: Context) {
        val store = store(context)
        val ringing = store.takeDue()
        apply(context, store)
        ringing.forEach { TimerRinger.ring(context, it) }
    }

    /**
     * Boot, an app update or a clock change: alarms are set again from what's stored. After a clock or time zone change
     * ([clockChanged]) each alarm's next ring is worked out again for the new clock (timers keep their end time).
     */
    fun restore(context: Context, clockChanged: Boolean = false) {
        val store = store(context)
        if (clockChanged) store.rearmAlarms() else apply(context, store)
    }

    private fun store(context: Context): TimersStore = (context.applicationContext as CompanionApp).timers

    private fun pending(context: Context, action: String, request: Int): PendingIntent = PendingIntent.getBroadcast(
        context, request, Intent(context, TimerReceiver::class.java).setAction(action),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )
}
