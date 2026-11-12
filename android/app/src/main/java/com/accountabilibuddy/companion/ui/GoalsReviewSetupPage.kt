package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.goals.GoalDisplayPrefs
import com.accountabilibuddy.companion.goals.GoalSettingsPrefs
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.shared.goals.GoalReviewRules
import java.time.DayOfWeek

/**
 * The migration assistant's page for the weekly goals review and goal reminders (since 0.1.78, round 2 R6b3): what they do, and the
 * review's day (picking one here is saved to Settings › Goals, which marks this set up). Suggestions only: nothing locks or waits.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun GoalsReviewSetupPage() {
    val context = LocalContext.current
    val review by GoalSettingsPrefs.review.collectAsStateWithLifecycle()
    val display by GoalDisplayPrefs.display.collectAsStateWithLifecycle()
    val shown = GoalReviewRules.reviewDay(review, display.weekStart)
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Goals: weekly review and reminders", style = MaterialTheme.typography.headlineSmall)
        Text("Once a week a card on Goals asks about each goal, one at a time: keep going, adjust the target, pause it, or decide next week. It suggests one from the numbers it shows (no AI, nothing leaves the phone), and every answer is kept as you go, so you can stop and pick it up later.")
        Text("• Snooze it for a day or two (in ProductaBuddy mode a snooze lasts the week). It never comes during a focus session.")
        Text("• Goal reminders: Edit goal › Reminders. They ring as notifications, skip a goal that is already done, and never show the goal's name or your reason on the notification.")
        Text("• \"Nudge when the week falls behind\" rings once, the morning after a goal drops below pace (on by default in AccountabiliBuddy mode, off in ProductaBuddy).")
        Text("Review day (now ${GoalReviewRules.dayPlural(shown)} at ${review.time.format(java.time.format.DateTimeFormatter.ofPattern("h:mm a", java.util.Locale.US))}; the time is in Settings › Goals):", style = MaterialTheme.typography.titleSmall)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf(DayOfWeek.SUNDAY, DayOfWeek.MONDAY, DayOfWeek.FRIDAY, DayOfWeek.SATURDAY).forEach { day ->
                AbChip(GoalReviewRules.dayPlural(day), selected = review.day == day,onClick = { GoalSettingsPrefs.setReview(context, review.copy(day = day)) })
            }
        }
        Text(
            "Other days are in Settings › Goals › Weekly review.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
