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
    private companion object {
        const val WAIT_FOR_FILE_MS = 8_000L
    }

    override fun onReceive(context: Context, intent: Intent) {
        val app = context.applicationContext as CompanionApp
        val pending = goAsync()
        CoroutineScope(Dispatchers.Default).launch {
            try {
                runCatching { app.phoneSession.controller.tick() }
                Refresher.refresh(app)
                // The heartbeat's job is the file (B13): stay awake until the passes are done (bounded, a receiver may not run long).
                if (intent.action == PhoneSessionAlarm.ACTION_HEARTBEAT && app.phoneSession.realSessionRunning()) app.sendOwnFileAndWait(WAIT_FOR_FILE_MS)
            } finally {
                pending.finish()
            }
        }
    }
}
