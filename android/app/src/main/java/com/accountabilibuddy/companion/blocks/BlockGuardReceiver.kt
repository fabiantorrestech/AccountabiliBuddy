package com.accountabilibuddy.companion.blocks

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

/** The block guard's exact alarm (ADR 37): a heads-up or a re-enable time came. */
class BlockGuardReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != BlockGuard.ACTION_RUN) return
        val pending = goAsync()
        CoroutineScope(SupervisorJob() + Dispatchers.Default).launch {
            try {
                // Whatever Curbox's binder throws must not kill the process before the next alarm is set (as Refresher does).
                runCatching { BlockGuard.run(context) }
            } finally {
                pending.finish()
            }
        }
    }
}
