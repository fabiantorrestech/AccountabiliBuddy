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
    /** The next alarm clock and the app that set it (from the alarm's own "open" intent); null when none is set. */
    fun read(context: Context): AlarmSighting? {
        val info = context.getSystemService(AlarmManager::class.java)?.nextAlarmClock ?: return null
        return AlarmSighting(Instant.ofEpochMilli(info.triggerTime), info.showIntent?.creatorPackage, Instant.now())
    }
}

/** Android says the next alarm changed: the phone's wake time, its sync file and the card follow. */
class NextAlarmReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == AlarmManager.ACTION_NEXT_ALARM_CLOCK_CHANGED) {
            (context.applicationContext as CompanionApp).onNextAlarmChanged()
        }
    }
}
