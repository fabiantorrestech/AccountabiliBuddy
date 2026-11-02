package com.accountabilibuddy.companion.ui

import android.Manifest
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.Composable
import com.accountabilibuddy.companion.pause.PauseState
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel

/**
 * The phone guard's less used switches: Pause in the car (needs Bluetooth to know it is connected to a car) and the work-start nudge
 * ([StartNudgeControls]). Settings › Guards › Phone guard › Advanced shows them. A guard section: view-only during a focus session.
 */
@Composable
fun PauseAdvancedControls(state: PauseState, actions: PauseActions) = GuardsScope {
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted -> actions.onCarPause(granted) }
    ToggleRow(
        "Pause in the car",
        "While the phone is connected to a car (its Bluetooth says car audio or hands-free, not just its name) or Android Auto runs: no nudges or parked phone, and unlocks don't count as pickups.",
        state.carPause,
    ) { on ->
        if (on && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) permission.launch(Manifest.permission.BLUETOOTH_CONNECT) else actions.onCarPause(on)
    }
    if (state.carPause && state.inCar) Note("In the car now.")
    StartNudgeControls(actions)
}

/**
 * The work-start nudge: how often it comes again, and the full-screen work card ("Take over the screen") that needs Android's
 * "Display over other apps". Shown in Settings › Focus sessions and in the phone guard's Advanced fold: the same two settings.
 */
@Composable
fun StartNudgeControls(actions: PauseActions) = GuardsScope {
    AbSectionLabel("Work-start nudge: again every")
    AbChipRow {
        listOf(5, 10, 15, 0).forEach { minutes ->
            GuardChip(if (minutes == 0) "Once" else "$minutes min", selected = actions.nudgeRepeat == minutes, onClick = { actions.onNudgeRepeat(minutes) })
        }
    }

    ToggleRow(
        "Take over the screen",
        "During work time with no session, unlocking the phone shows a full-screen card: plan 3 tasks and start. " +
            "\"I'm starting\" gives 10 minutes, snoozing is free twice a day, then needs a reason.",
        actions.takeoverOn,
    ) { actions.onTakeover(it) }
    if (actions.takeoverOn && !actions.takeoverAllowed) {
        GuardTonalButton("Allow \"Display over other apps\"", onClick = actions.onAllowTakeover)
    }
}
