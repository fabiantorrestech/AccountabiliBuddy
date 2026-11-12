package com.accountabilibuddy.companion.ui.goals

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.EventRepeat
import androidx.compose.material.icons.outlined.Snooze
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.goals.PhoneGoalReview
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalReviewSnooze

/**
 * The weekly review's entry card on the Goals list, above "YOUR GOALS" (round 2 R6b3; audit list "R6b3 · weekly review" item 1, change note
 * §4 "Weekly review card"): the icon, "Weekly goals review · 8 goals", "About 4 minutes · Sundays", **Start** (38 dp) and the snooze button
 * (alarm-snooze, 36 dp) with its menu next to it. Shown from Settings › Goals' review day and time until every goal has an answer or it is
 * snoozed; in ProductaBuddy mode a snoozed review keeps a quiet card ("Snoozed · start it whenever you like"). Never during a session.
 */
@Composable
fun GoalReviewCard(moment: PhoneGoalReview.Moment, defaultSnoozeDays: Int, onStart: () -> Unit, onSnooze: (GoalReviewSnooze) -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val row = moment.row
    var menu by rememberSaveable { mutableStateOf(false) }
    AbCard(modifier, contentPadding = PaddingValues(start = 16.dp, end = 8.dp, top = 12.dp, bottom = 12.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Icon(Icons.Outlined.EventRepeat, contentDescription = null, tint = if (row.snoozed) colors.faint else colors.accent, modifier = Modifier.size(24.dp))
            Column(Modifier.weight(1f)) {
                Text(row.title, style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = colors.ink, maxLines = 2, overflow = TextOverflow.Ellipsis)
                Text(row.note, style = Ab.type.small, color = colors.muted, maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
            Surface(
                onClick = onStart,
                shape = Ab.shapes.pill,
                color = if (row.snoozed) colors.surface2 else colors.accent,
                contentColor = if (row.snoozed) colors.ink else colors.onAccent,
                modifier = Modifier.height(38.dp),
            ) {
                Box(Modifier.padding(horizontal = 16.dp), contentAlignment = Alignment.Center) {
                    Text("Start", style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold)
                }
            }
            if (row.canSnooze) {
                Box {
                    IconButton(onClick = { menu = true }, modifier = Modifier.size(36.dp)) {
                        Icon(Icons.Outlined.Snooze, contentDescription = "Snooze the weekly review", tint = colors.muted, modifier = Modifier.size(22.dp))
                    }
                    GoalReviewSnoozeMenu(menu, moment, defaultSnoozeDays, onDismiss = { menu = false }, onPick = { menu = false; onSnooze(it) })
                }
            }
        }
    }
}
