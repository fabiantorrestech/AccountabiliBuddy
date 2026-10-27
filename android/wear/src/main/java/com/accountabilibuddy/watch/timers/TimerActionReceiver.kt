package com.accountabilibuddy.watch.timers

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/** The "Time's up" notification's Dismiss and +1 min. */
class TimerActionReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val id = intent.getIntExtra(TimerEndReceiver.ID, -1)
        // The store's alarms and notification are updated on a background thread: keep the process until that is done.
        val pending = goAsync()
        when (intent.action) {
            TimerRing.ACTION_DISMISS -> TimerStore.dismiss(context, id)
            TimerRing.ACTION_PLUS_MINUTE -> TimerStore.plusMinute(context, id)
        }
        TimerStore.afterPendingWork { pending.finish() }
    }
}
