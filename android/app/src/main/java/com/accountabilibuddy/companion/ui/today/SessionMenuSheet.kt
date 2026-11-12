package com.accountabilibuddy.companion.ui.today

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.DirectionsWalk
import androidx.compose.material.icons.outlined.Flag
import androidx.compose.material.icons.outlined.MoreTime
import androidx.compose.material.icons.outlined.Pause
import androidx.compose.material.icons.outlined.StopCircle
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.guidance.isCompanionMode
import com.accountabilibuddy.companion.sync.PcState
import com.accountabilibuddy.companion.ui.StepAwayState
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.rememberTickingNow
import com.accountabilibuddy.companion.ui.secondsLeft
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * ⋯ › Session (brief §4.1, board Today-Phone-Session-Menu): a half-height sheet with the PC's Session menu rows, for the PC session
 * Today shows. Only what a PC takes from the phone today is live (plan §3b item 1, ADR 42): Step away / I'm back and End session,
 * through the same paths Today always used ([StepAwayState.toggle], [onEndEarly] = the end-early request with the PC's own wait and
 * reason). Pause timer, Add 10 minutes and End and mark the rest unfinished are greyed "Coming soon" until ADR 82 is accepted; nothing
 * new is sent. End session asks first, then starts the normal end-early flow: it is never a shortcut, and the PC's answer comes back
 * as the usual message.
 */
@Composable
fun SessionMenuSheet(
    state: PcState,
    pcName: String?,
    clock: ClockFormat,
    awaySince: Instant?,
    stepAway: StepAwayState?,
    /** Why End session can't be asked now (an update needed, an answer still awaited); null when it can. */
    endBlocked: String?,
    onEndEarly: () -> Unit,
    onDismiss: () -> Unit,
) {
    val companion = isCompanionMode()
    var confirmingEnd by remember { mutableStateOf(false) }
    val pc = pcName ?: "the PC"
    val soon = "Coming soon for a session on $pc"

    AbSheet(title = "Session", subtitle = sessionSubtitle(state, clock, awaySince), onDismiss = onDismiss, height = AbSheetHeight.Half) {
        val away = stepAway?.away == true
        AbSheetRow(
            title = if (away) "I'm back" else "Step away",
            subtitle = stepAway?.text ?: if (away) "Back to the session on $pc" else "Timer keeps running · logged as away",
            icon = Icons.AutoMirrored.Outlined.DirectionsWalk,
            enabled = stepAway != null && !stepAway.sending,
            onClick = {
                // The session card then says "Stepping away · sending…" until the PC's file shows it (a PC writes no answer for these).
                stepAway?.toggle?.invoke()
                onDismiss()
            },
        )
        AbSheetRow(title = "Pause timer", subtitle = soon, icon = Icons.Outlined.Pause, enabled = false, onClick = {})
        AbSheetRow(title = "Add 10 minutes", subtitle = soon, icon = Icons.Outlined.MoreTime, enabled = false, onClick = {})
        AbSheetRow(title = "End and mark the rest unfinished", subtitle = soon, icon = Icons.Outlined.Flag, enabled = false, onClick = {})
        AbSheetRow(
            title = "End session",
            subtitle = endBlocked ?: if (companion) "The normal end-early flow" else "The normal end-early flow (wait and reason)",
            icon = Icons.Outlined.StopCircle,
            danger = true,
            enabled = endBlocked == null,
            onClick = { confirmingEnd = true },
        )
    }

    if (confirmingEnd) {
        AbDialog(
            title = "End the session?",
            onDismiss = { confirmingEnd = false },
            confirmText = "Ask to end",
            onConfirm = {
                confirmingEnd = false
                onEndEarly()
                onDismiss()
            },
            dismissText = "Keep going",
        ) {
            Text(
                if (companion) {
                    "This asks $pc to end the session. It answers when it next syncs."
                } else {
                    "This asks $pc to end early. Its wait and a reason come next, as always; the session keeps running until then."
                },
                style = Ab.type.bodyMedium, color = Ab.colors.ink,
            )
        }
    }
}

// "32m left · ends 1:23 PM · Deep work", redrawn each second in the sheet only.
@Composable
private fun sessionSubtitle(state: PcState, clock: ClockFormat, awaySince: Instant?): String {
    val ticking by rememberTickingNow()
    val now = awaySince?.takeIf { it.isBefore(ticking) } ?: ticking
    val end = state.phaseEndsAt?.toInstant()
    val parts = mutableListOf<String>()
    if (end != null) {
        parts += "${TimeLeftText.compact(secondsLeft(now, end))} left"
        if (awaySince == null && !state.pcOff) parts += "ends ${clock.time(LocalDateTime.ofInstant(end, ZoneId.systemDefault()))}"
    } else {
        parts += "No set end"
    }
    state.profileName?.let { parts += it }
    return parts.joinToString(" · ")
}
