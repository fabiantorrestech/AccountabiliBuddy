package com.accountabilibuddy.companion.notify

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

/** The refresh alarm, boot, app update and clock changes: draw the notification and widget again. */
class RefreshReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val pending = goAsync()
        CoroutineScope(Dispatchers.Default).launch {
            try {
                Refresher.refresh(context.applicationContext)
            } finally {
                pending.finish()
            }
        }
    }
}
