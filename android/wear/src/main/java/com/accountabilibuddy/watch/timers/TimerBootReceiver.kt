package com.accountabilibuddy.watch.timers

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/**
 * After the watch restarts or the app is updated its alarms are gone: the running timers' are set again (one that ended
 * meanwhile shows as done).
 */
class TimerBootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_BOOT_COMPLETED || intent.action == Intent.ACTION_MY_PACKAGE_REPLACED) {
            TimerStore.refresh(context)
        }
    }
}
