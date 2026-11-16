package com.accountabilibuddy.companion.ui.today

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.DirectionsWalk
import androidx.compose.material.icons.outlined.Flag
import androidx.compose.material.icons.outlined.MoreTime
import androidx.compose.material.icons.outlined.Pause
import androidx.compose.material.icons.outlined.PlayArrow
import androidx.compose.material.icons.outlined.StopCircle
import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.ui.StepAwayState
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.shared.session.PhoneAwayKind
import com.accountabilibuddy.shared.session.PhoneSessionEngine
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.session.PhoneSessionResult
import com.accountabilibuddy.shared.session.PhoneSessionTexts

/** The words for an engine answer that did nothing (null when it did something): what a Session menu action says when it was refused. */
fun refusalText(result: PhoneSessionResult): String? = (result as? PhoneSessionResult.Refused)?.let { PhoneSessionTexts.refusal(it.reason) }

/**
 * ⋯ › Session for the phone's OWN session (board Today-Phone-Session-Menu, PS3a): the same rows as a PC session's menu, live for a session on this phone
 * (plan R7, R10): Step away / I'm back, Pause timer / Resume, Add 10 minutes, then End session. Every row is one controller call, at once (nothing is asked of a
 * PC). "End and mark the rest unfinished" stays greyed, as for a PC session (ADR 85: not for a phone session). End session opens the End sheet, which holds the
 * wait and the reason of AccountabiliBuddy ([EndPhoneSessionSheet]); it is never a shortcut.
 */
@Composable
fun OwnSessionMenuSheet(
    record: PhoneSessionRecord,
    clock: ClockFormat,
    stepAway: StepAwayState,
    onMessage: (String) -> Unit,
    onEnd: () -> Unit,
    onDismiss: () -> Unit,
) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val controller = app.phoneSession.controller
    val state = OwnSessionView.pcState(record)
    val paused = record.openAway?.kind == PhoneAwayKind.PAUSED
    val stretch = record.openAway?.kind
    // The engine's own answer, asked without applying it: a row is live exactly when the engine would do it.
    val add10 = PhoneSessionEngine.add10(record, controller.now())

    fun run(result: PhoneSessionResult) {
        refusalText(result)?.let(onMessage)
        onDismiss()
    }

    AbSheet(title = "Session", subtitle = sessionSubtitle(state, clock, OwnSessionView.pausedSince(record), OwnSessionView.NAME), onDismiss = onDismiss, height = AbSheetHeight.Half) {
        val away = stepAway.away
        AbSheetRow(
            title = if (away) "I'm back" else "Step away",
            subtitle = stepAway.text ?: when {
                stepAway.blocked -> "The timer is paused"
                away -> "Back to the session"
                else -> "Logged as away; the timer keeps running"
            },
            icon = Icons.AutoMirrored.Outlined.DirectionsWalk,
            enabled = stepAway.enabled,
            onClick = { stepAway.toggle(); onDismiss() },
        )
        AbSheetRow(
            title = if (paused) "Resume timer" else "Pause timer",
            subtitle = when {
                paused -> "The end moves later by the pause"
                stretch == PhoneAwayKind.LUNCH -> "Paused for lunch"
                stretch == PhoneAwayKind.DEVICE_OFF -> "Paused: the phone was off"
                stretch == PhoneAwayKind.STEP_AWAY -> "Come back first"
                else -> "Freezes the countdown; the locks stay"
            },
            icon = if (paused) Icons.Outlined.PlayArrow else Icons.Outlined.Pause,
            enabled = paused || !record.isAway,
            onClick = { run(if (paused) controller.resume() else controller.pause()) },
        )
        AbSheetRow(
            title = "Add 10 minutes",
            subtitle = (add10 as? PhoneSessionResult.Refused)?.let { PhoneSessionTexts.refusal(it.reason) } ?: "At once; the locks are asked again for the new end",
            icon = Icons.Outlined.MoreTime,
            enabled = add10 is PhoneSessionResult.Done,
            onClick = { run(controller.add10()) },
        )
        AbSheetRow(title = "End and mark the rest unfinished", subtitle = "Not for a session on this phone", icon = Icons.Outlined.Flag, enabled = false, onClick = {})
        AbSheetRow(
            title = "End session",
            subtitle = if (record.armedEnd != null) "Ending by itself; open to cancel" else "Opens the end sheet",
            icon = Icons.Outlined.StopCircle,
            danger = true,
            onClick = { onDismiss(); onEnd() },
        )
    }
}
