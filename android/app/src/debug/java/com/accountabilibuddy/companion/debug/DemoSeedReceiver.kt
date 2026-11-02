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
        const val MODE_PLANNING = "planning"
        const val MODE_TWOPC = "twopc"
        const val MODE_AWAY = "away"
        const val MODE_AWAY_PAST = "away-past"
        const val MODE_AWAY_STALE = "away-stale"
        const val MODE_AWAY_OFFLINE = "away-offline"
        const val MODE_SESSION_BUDGET = "session-budget"
        const val MODE_BUDGET = "budget"
        const val MODE_FREE = "free"
        const val MODE_DAYOFF = "dayoff"
        const val MODE_CLEAR = "clear"

        // Part 4 (Plan): a mixed week (Workday, a Light Thursday, Off weekend) with calendar events (`plan`), the same with a
        // running session (`plan-session`), and a plan edit the PC is holding for its wait (`plan-wait`, ADR 28).
        const val MODE_PLAN = "plan"
        const val MODE_PLAN_SESSION = "plan-session"
        const val MODE_PLAN_WAIT = "plan-wait"

        // Part 3 (Tasks): the lists the board shows (`tasks`, `tasks-session`), a read of what the phone's own sync file carries
        // (ids and times only, never text) and a run of the Later merge rules against the real store.
        const val MODE_TASKS = "tasks"
        const val MODE_TASKS_SESSION = "tasks-session"
        const val MODE_FOLDER_DROP = "folder-drop"
        const val MODE_FOLDER_AGE = "folder-age"
        const val MODE_DUMP = "dump"
        const val MODE_MERGE_CHECK = "merge-check"
        const val MODE_HANDOFF_CHECK = "handoff-check"
        const val MODE_DONE_CHECK = "done-check"
    }
}
