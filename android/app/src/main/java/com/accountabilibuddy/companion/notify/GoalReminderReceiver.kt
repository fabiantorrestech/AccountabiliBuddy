package com.accountabilibuddy.companion.notify

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.accountabilibuddy.companion.CompanionApp
import java.util.UUID
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

/** Goal reminders' alarm (ring what's due) and their notifications' Tick today and Snooze, and the review's Snooze (round 2 R6b3). */
class GoalReminderReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val pending = goAsync()
        CoroutineScope(Dispatchers.Default).launch {
            try {
                val app = context.applicationContext as CompanionApp
                val key = intent.getStringExtra(GoalReminders.EXTRA_KEY)
                val id = intent.getIntExtra(GoalReminders.EXTRA_ID, 0)
                when (intent.action) {
                    GoalReminders.ACTION_FIRE -> GoalReminders.refresh(context, app.pause.isQuiet)
                    GoalReminders.ACTION_SNOOZE -> if (key != null) GoalReminders.snooze(context, key, id)
                    GoalReminders.ACTION_TICK -> {
                        val goal = intent.getStringExtra(GoalReminders.EXTRA_GOAL)?.let { runCatching { UUID.fromString(it) }.getOrNull() }
                        if (goal != null && key != null) GoalReminders.tick(context, goal, key, id)
                    }
                    GoalReminders.ACTION_REVIEW_SNOOZE -> GoalReminders.snoozeReview(context)
                }
            } finally {
                pending.finish()
            }
        }
    }
}
