package com.accountabilibuddy.companion.session

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.notify.Refresher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

/** The phone session's end alarm and armed-end alarm: the controller asks the engine, which ends the session only when its rules say so. */
class PhoneSessionAlarmReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val app = context.applicationContext as CompanionApp
        val pending = goAsync()
        CoroutineScope(Dispatchers.Default).launch {
            try {
                runCatching { app.phoneSession.controller.tick() }
                Refresher.refresh(app)
            } finally {
                pending.finish()
            }
        }
    }
}
