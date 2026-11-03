package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.steps.StepsProblem
import com.accountabilibuddy.companion.steps.StepsState
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.steps.StepsStore
import com.accountabilibuddy.companion.ui.kit.AbToggleRow
import com.accountabilibuddy.companion.ui.kit.AbValueButton
import com.accountabilibuddy.companion.ui.theme.Ab
import java.text.NumberFormat

/**
 * Settings → Your day → "Daily steps": the "Track daily steps" switch (off by default; turning it on asks Health Connect for the steps
 * permission) and the goal. The count shows on Today and in Stats, and goes to your own devices in this phone's sealed sync file.
 */
@Composable
internal fun StepsSection(steps: StepsState) {
    val store = (LocalContext.current.applicationContext as CompanionApp).steps
    val numbers = NumberFormat.getIntegerInstance()
    Section("Daily steps", summary = if (steps.enabled) "Goal ${numbers.format(steps.goal)}" else "Off") {
        Note("Reads your steps from Health Connect at most every few minutes: while the app is open, and in the background too when Health Connect lets you allow that. The count shows on Today and in Stats, and goes to your own devices in the sealed sync file; nowhere else, and not to the watch.")
        AbToggleRow("Track daily steps", steps.enabled, onCheckedChange = store::setEnabled, subtitle = "Asks Health Connect for the steps permission")
        if (steps.enabled && steps.problem == StepsProblem.NeedsPermission) {
            Note("Health Connect hasn't allowed steps yet.")
            AbTonalButton("Allow Health Connect steps", onClick = store::askAgain)
        }
        if (steps.enabled && steps.problem == StepsProblem.NotAvailable) Note("Health Connect isn't available on this phone.")
        if (steps.enabled) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                AbValueButton("−500", onClick = { store.setGoal(steps.goal - StepsStore.GOAL_STEP) }, enabled = steps.goal > StepsStore.MIN_GOAL)
                Text("${numbers.format(steps.goal)} steps", style = Ab.type.timerSmall, color = Ab.colors.ink)
                AbValueButton("+500", onClick = { store.setGoal(steps.goal + StepsStore.GOAL_STEP) }, enabled = steps.goal < StepsStore.MAX_GOAL)
            }
        }
    }
}
