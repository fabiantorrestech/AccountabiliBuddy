package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.today.STEPS_GOAL_EXISTS
import com.accountabilibuddy.companion.ui.today.addStepsGoal

/**
 * The migration assistant's page for steps in Goals (since 0.1.78, TD1): Today no longer has a steps ring; a Steps goal with Show on
 * Today shows them in Today's goals. One tap adds that goal (a day's target as set in Settings › Your day, 8,000 by default); nothing
 * is added by itself. Adding it (or closing the card on Today) marks this set up.
 */
@Composable
internal fun StepsInGoalsSetupPage() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    var message by remember { mutableStateOf<String?>(null) }
    var added by remember { mutableStateOf(false) }
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Steps now live in Goals", style = MaterialTheme.typography.headlineSmall)
        Text("The step ring is gone from Today. Your steps are seen and tracked through a Steps goal: with Show on Today on, it appears in Today's goals.")
        Text("Counting still needs Track daily steps (Settings → Your day) and Health Connect, as before.")
        AbTonalButton("Add a steps goal", enabled = !added, onClick = {
            val error = addStepsGoal(app)
            message = error ?: "Added. Find it under Goals."
            if (error == null || error == STEPS_GOAL_EXISTS) {
                added = true
                UiPrefs.finishStepsMoved(context)
            }
        })
        message?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }
    }
}
