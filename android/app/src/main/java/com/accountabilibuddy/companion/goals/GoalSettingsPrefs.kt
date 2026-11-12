package com.accountabilibuddy.companion.goals

import android.content.Context
import com.accountabilibuddy.shared.goals.GoalReminderDefaults
import com.accountabilibuddy.shared.goals.GoalReviewSettings
import java.time.DayOfWeek
import java.time.LocalTime
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * What Settings › Goals keeps on this phone (round 2 R6b2; the PC's "goals.settings" review and reminder defaults): the weekly review's
 * day, time, snooze and tidy-up choice, and the rule switches a new goal reminder starts with (the nudge has one default per mode,
 * ADR 69). Suggestions only: nothing here locks, waits or feeds a guard. Kept in the preferences "goalsUi" next to the Goals screen's
 * own choices ([GoalDisplayPrefs]), so it travels in the settings backup's Look group; this phone only, never synced. Loaded at app
 * start; an unreadable value reads as its default. The review and the reminders that use these arrive in R6b3.
 */
object GoalSettingsPrefs {
    private const val REVIEW_DAY = "review.day"
    private const val REVIEW_TIME = "review.time"
    private const val REVIEW_SNOOZE = "review.snoozeDays"
    private const val REVIEW_TIDY = "review.tidyUpAfter"
    private const val SKIP = "reminders.skipWhenDone"
    private const val NUDGE_AB = "reminders.nudgeAccountabiliBuddy"
    private const val NUDGE_PB = "reminders.nudgeProductaBuddy"

    private val _review = MutableStateFlow(GoalReviewSettings())
    private val _reminders = MutableStateFlow(GoalReminderDefaults())

    val review: StateFlow<GoalReviewSettings> = _review.asStateFlow()
    val reminders: StateFlow<GoalReminderDefaults> = _reminders.asStateFlow()

    fun load(context: Context) {
        val prefs = context.getSharedPreferences(GoalDisplayPrefs.FILE, Context.MODE_PRIVATE)
        _review.value = GoalReviewSettings(
            day = prefs.getString(REVIEW_DAY, null)?.let { name -> DayOfWeek.entries.firstOrNull { it.name == name } },
            time = prefs.getString(REVIEW_TIME, null)?.let { runCatching { LocalTime.parse(it) }.getOrNull() } ?: LocalTime.of(18, 0),
            snoozeDays = prefs.getInt(REVIEW_SNOOZE, 1),
            tidyUpAfter = prefs.getBoolean(REVIEW_TIDY, false),
        ).clamped()
        _reminders.value = GoalReminderDefaults(
            skipWhenDone = prefs.getBoolean(SKIP, true),
            nudgeAccountabiliBuddy = prefs.getBoolean(NUDGE_AB, true),
            nudgeProductaBuddy = prefs.getBoolean(NUDGE_PB, false),
        )
    }

    fun setReview(context: Context, review: GoalReviewSettings) {
        val clamped = review.clamped()
        _review.value = clamped
        context.getSharedPreferences(GoalDisplayPrefs.FILE, Context.MODE_PRIVATE).edit().apply {
            if (clamped.day == null) remove(REVIEW_DAY) else putString(REVIEW_DAY, clamped.day!!.name)
            putString(REVIEW_TIME, clamped.time.toString())
            putInt(REVIEW_SNOOZE, clamped.snoozeDays)
            putBoolean(REVIEW_TIDY, clamped.tidyUpAfter)
        }.apply()
    }

    fun setReminders(context: Context, reminders: GoalReminderDefaults) {
        _reminders.value = reminders
        context.getSharedPreferences(GoalDisplayPrefs.FILE, Context.MODE_PRIVATE).edit()
            .putBoolean(SKIP, reminders.skipWhenDone)
            .putBoolean(NUDGE_AB, reminders.nudgeAccountabiliBuddy)
            .putBoolean(NUDGE_PB, reminders.nudgeProductaBuddy)
            .apply()
    }
}
