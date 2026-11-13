package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalStepsRules
import java.time.OffsetDateTime
import java.util.UUID

/**
 * The one-time quiet card on Today (TD1): "Steps now live in Goals". Daily steps left Today; a Steps goal with Show on Today shows
 * them in Today's goals. One tap on "Add a steps goal" makes it (a day's target as set in Settings › Your day, 8,000 by default, Show on
 * Today on); × closes the card for good. Nothing is made by itself. Shown by [com.accountabilibuddy.companion.today.StepsMovedRules].
 */
@Composable
fun StepsMovedCard(onDone: () -> Unit, onMessage: (String) -> Unit) {
    val app = LocalContext.current.applicationContext as CompanionApp
    AbCard(Modifier.fillMaxWidth()) {
        Row(verticalAlignment = Alignment.Top, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Steps now live in Goals", style = Ab.type.bodyMedium, color = Ab.colors.ink)
                Text("Add a steps goal to see your steps in Today's goals.", style = Ab.type.small, color = Ab.colors.muted)
                AbTonalButton("Add a steps goal", onClick = {
                    val error = addStepsGoal(app)
                    if (error == null || error == STEPS_GOAL_EXISTS) onDone()
                    if (error != null) onMessage(error)
                })
            }
            IconButton(onClick = onDone) { Icon(Icons.Outlined.Close, contentDescription = "Close", tint = Ab.colors.muted) }
        }
    }
}

// Makes the goal in the first live area, after that area's goals; returns the reason it couldn't, or null when it is made.
// Never a second one: a steps goal that already exists (made here a moment ago, on another device, or by hand) is kept as the only one,
// so a second tap here or on the migration assistant's page adds nothing.
internal fun addStepsGoal(app: CompanionApp): String? {
    val lists = app.goals.lists.value
    if (GoalStepsRules.hasStepsGoal(lists.liveGoals)) return STEPS_GOAL_EXISTS
    val area = GoalStepsRules.areaFor(lists) ?: return "Goals aren't set up yet."
    val position = (lists.liveGoals.filter { it.areaId == area.id }.maxOfOrNull { it.position } ?: 0.0) + 1.0
    val daily = app.steps.state.value.goal
    return app.goals.addGoal(GoalStepsRules.newGoal(UUID.randomUUID(), area.id, position, OffsetDateTime.now(), daily))
}

/** What [addStepsGoal] says when a steps goal already exists. */
internal const val STEPS_GOAL_EXISTS = "You already have a steps goal. Find it under Goals."
