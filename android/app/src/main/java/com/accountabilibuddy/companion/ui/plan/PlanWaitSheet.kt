package com.accountabilibuddy.companion.ui.plan

import androidx.compose.runtime.Composable
import com.accountabilibuddy.companion.sync.PlanWait
import com.accountabilibuddy.companion.ui.kit.ChangeWaitsSheet

/**
 * The "change waits" sheet (board 5h) for a plan edit a PC took and is holding (ADR 28: it frees part of today's work hours).
 * The wait already started on the PC, so the sheet only says when it applies on its own; there is no "apply now", and the
 * way to cancel is the PC's own banner.
 */
@Composable
fun PlanWaitSheet(wait: PlanWait, onDismiss: () -> Unit) {
    ChangeWaitsSheet(
        title = "Your plan change waits",
        body = "${wait.pcName} is holding it for ${wait.minutes} minutes because it frees part of today's work hours. If you still want it then, it applies on its own.",
        appliesAt = wait.appliesAt,
        about = true,
        cancelNote = "cancel it on ${wait.pcName} before then",
        onDismiss = onDismiss,
    )
}
