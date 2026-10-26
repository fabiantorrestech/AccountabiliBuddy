package com.accountabilibuddy.companion.timers

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.companion.CompanionApp

/** The timers' exact alarm and progress tick, and the buttons on their notifications. */
class TimerReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val store = (context.applicationContext as CompanionApp).timers
        val id = intent.getIntExtra(EXTRA_ID, -1)
        when (intent.action) {
            TimerScheduler.ACTION_FIRE -> TimerScheduler.fire(context)
            TimerScheduler.ACTION_TICK, ACTION_REPOST -> TimerScheduler.apply(context, store)
            ACTION_PAUSE -> store.pause(id)
            ACTION_RESUME -> store.resume(id)
            ACTION_ADD_MINUTE -> store.addMinute(id)
            ACTION_DISMISS_TIMER -> store.reset(id)
            ACTION_DISMISS_ALARM -> store.dismissAlarm(id)
            ACTION_SNOOZE_ALARM -> store.snoozeAlarm(id)
        }
    }

    companion object {
        const val EXTRA_ID = "id"
        const val ACTION_REPOST = "com.accountabilibuddy.companion.TIMER_REPOST"
        const val ACTION_PAUSE = "com.accountabilibuddy.companion.TIMER_PAUSE"
        const val ACTION_RESUME = "com.accountabilibuddy.companion.TIMER_RESUME"
        const val ACTION_ADD_MINUTE = "com.accountabilibuddy.companion.TIMER_ADD_MINUTE"
        const val ACTION_DISMISS_TIMER = "com.accountabilibuddy.companion.TIMER_DISMISS"
        const val ACTION_DISMISS_ALARM = "com.accountabilibuddy.companion.ALARM_DISMISS"
        const val ACTION_SNOOZE_ALARM = "com.accountabilibuddy.companion.ALARM_SNOOZE"
    }
}
