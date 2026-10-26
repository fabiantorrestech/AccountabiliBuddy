package com.accountabilibuddy.companion.notify

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.companion.engine.ClockFormat

/** The bedtime reminders' alarm (fire what's due) and the alarm notification's Dismiss and Snooze (ADR 38). */
class BedtimeReminderReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val number = intent.getIntExtra(BedtimeReminders.EXTRA_NUMBER, 0)
        when (intent.action) {
            BedtimeReminders.ACTION_FIRE -> BedtimeReminders.fire(context, ClockFormat(is24Hour = false))
            BedtimeReminders.ACTION_DISMISS -> BedtimeReminders.dismiss(context, number)
            BedtimeReminders.ACTION_SNOOZE -> BedtimeReminders.snooze(context, number)
        }
    }
}
