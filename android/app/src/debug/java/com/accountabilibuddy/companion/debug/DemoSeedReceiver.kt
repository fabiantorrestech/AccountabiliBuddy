package com.accountabilibuddy.companion.debug

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import com.accountabilibuddy.companion.CompanionApp
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

/**
 * Debug builds only (this file is in the debug source set, so a release APK doesn't contain it): fills the app with made-up
 * data for screenshots on an emulator.
 *
 * `adb -s emulator-5554 shell am broadcast -a com.accountabilibuddy.companion.debug.SEED -p com.accountabilibuddy.companion
 * --es mode session` with mode `idle`, `session` or `clear` (see [DemoSeeder]). The result is written to logcat, tag DemoSeed.
 */
class DemoSeedReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val mode = intent.getStringExtra(EXTRA_MODE) ?: MODE_IDLE
        val pending = goAsync()
        CoroutineScope(Dispatchers.IO).launch {
            try {
                val app = context.applicationContext as CompanionApp
                Log.i(TAG, DemoSeeder(app).run(mode))
            } catch (e: Exception) {
                Log.e(TAG, "failed", e)
            } finally {
                pending.finish()
            }
        }
    }

    companion object {
        const val TAG = "DemoSeed"
        const val EXTRA_MODE = "mode"
        const val MODE_IDLE = "idle"
        const val MODE_SESSION = "session"
        const val MODE_CLEAR = "clear"
    }
}
