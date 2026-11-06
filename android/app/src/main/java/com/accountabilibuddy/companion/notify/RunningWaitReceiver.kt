package com.accountabilibuddy.companion.notify

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.companion.CompanionApp
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

/**
 * Waits running in the background (ADR 66): the pinned notification's Cancel ([ACTION_CANCEL]) and the alarm that sends the phone's
 * own armed end once the PC's wait allows it ([ACTION_SEND]). The notification is drawn again after either.
 */
class RunningWaitReceiver : BroadcastReceiver() {
    companion object {
        const val ACTION_CANCEL = "com.accountabilibuddy.companion.RUNNING_WAIT_CANCEL"
        const val ACTION_SEND = "com.accountabilibuddy.companion.RUNNING_WAIT_SEND"
        const val EXTRA_KEY = "key"
    }

    override fun onReceive(context: Context, intent: Intent) {
        val app = context.applicationContext as CompanionApp
        when (intent.action) {
            ACTION_CANCEL -> intent.getStringExtra(EXTRA_KEY)?.let { RunningWaits.cancel(app, it) }
            ACTION_SEND -> app.pcRemote.sendArmedIfDue()
        }

        val pending = goAsync()
        CoroutineScope(Dispatchers.Default).launch {
            try {
                Refresher.refresh(app)
            } finally {
                pending.finish()
            }
        }
    }
}
