package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.layout.Column
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.layout.size
import androidx.compose.ui.Modifier
import com.accountabilibuddy.companion.goals.PhoneGoalReview
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalReviewRules
import com.accountabilibuddy.shared.goals.GoalReviewSnooze
import java.time.ZoneId

/**
 * The weekly review's snooze menu (board Goals-Phone-Review), opened next to its button: "Tomorrow · Mon, Nov 16 · 9:00 AM", "In 2 days ·
 * Tue, Nov 17 · 9:00 AM" (Settings › Goals' snooze length is ticked), "Nudges… · Remind me once · then quiet". In ProductaBuddy mode a
 * snooze stays for the week (brief §5.10), so the menu offers that one choice.
 */
@Composable
fun GoalReviewSnoozeMenu(expanded: Boolean, moment: PhoneGoalReview.Moment, defaultDays: Int, onDismiss: () -> Unit, onPick: (GoalReviewSnooze) -> Unit) {
    val zone = ZoneId.systemDefault()
    DropdownMenu(expanded = expanded, onDismissRequest = onDismiss, containerColor = Ab.colors.surface2) {
        fun line(choice: GoalReviewSnooze) = GoalReviewRules.snoozeLine(GoalReviewRules.snoozeUntil(choice, moment.productaBuddy, moment.due, moment.now, zone), zone)
        if (moment.productaBuddy) {
            Item("For this week", "Start it whenever you like", checked = true) { onPick(GoalReviewSnooze.Tomorrow) }
            return@DropdownMenu
        }
        Item("Tomorrow", line(GoalReviewSnooze.Tomorrow), checked = defaultDays == 1) { onPick(GoalReviewSnooze.Tomorrow) }
        Item("In 2 days", line(GoalReviewSnooze.TwoDays), checked = defaultDays == 2) { onPick(GoalReviewSnooze.TwoDays) }
        Item("Nudges…", "Remind me once · then quiet", checked = false) { onPick(GoalReviewSnooze.RemindOnce) }
    }
}

@Composable
private fun Item(title: String, line: String, checked: Boolean, onClick: () -> Unit) {
    DropdownMenuItem(
        text = {
            Column {
                Text(title, style = Ab.type.body, color = Ab.colors.ink)
                Text(line, style = Ab.type.micro, color = Ab.colors.muted)
            }
        },
        trailingIcon = if (checked) ({ Icon(Icons.Outlined.Check, contentDescription = "Your usual snooze", tint = Ab.colors.accent, modifier = Modifier.size(18.dp)) }) else null,
        onClick = onClick,
    )
}
