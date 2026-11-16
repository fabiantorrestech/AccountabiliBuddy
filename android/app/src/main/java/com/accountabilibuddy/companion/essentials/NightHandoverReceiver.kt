package com.accountabilibuddy.companion.essentials

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.companion.notify.Refresher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

/** The Snooze and Cancel buttons of the "Night routine starts in 5 minutes" notification. */
class NightHandoverReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.action) {
            NightHandover.ACTION_SNOOZE -> NightHandover.snooze(context)
            NightHandover.ACTION_CANCEL -> NightHandover.cancel(context)
            else -> return
        }
        CoroutineScope(Dispatchers.Default).launch { runCatching { Refresher.refresh(context) } }
    }
}
