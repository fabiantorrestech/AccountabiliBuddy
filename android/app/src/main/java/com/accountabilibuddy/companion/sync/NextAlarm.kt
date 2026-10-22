package com.accountabilibuddy.companion.sync

import android.app.AlarmManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.companion.CompanionApp
import java.time.Instant

/**
 * The phone's next alarm (ADR 23): the one alarm apps (Clock, QR Alarm…) set as an alarm clock, which Android shares
 * with every app without a permission. It's the phone's wake time and goes to the PCs in its sync file.
 */
object NextAlarm {
    fun read(context: Context): Instant? =
        context.getSystemService(AlarmManager::class.java)?.nextAlarmClock?.triggerTime?.let(Instant::ofEpochMilli)
}

/** Android says the next alarm changed: the phone's wake time, its sync file and the card follow. */
class NextAlarmReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == AlarmManager.ACTION_NEXT_ALARM_CLOCK_CHANGED) {
            (context.applicationContext as CompanionApp).onNextAlarmChanged()
        }
    }
}
