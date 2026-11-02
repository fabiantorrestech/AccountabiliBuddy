package com.accountabilibuddy.companion.ui

import android.widget.Toast
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Step away or I'm back from the phone during a PC session (ADR 26, sent through the sync files): [away] is where the session
 * stands, [sending] that the request is still on its way, [text] the line under the timer ("Away from Work PC since 2:05 PM"; null
 * when there is nothing to say), and [toggle] sends the other one. The PC applies it within 10 minutes of the tap, or not at all; it
 * never shortens a wait or unlocks anything.
 */
class StepAwayState(val away: Boolean, val sending: Boolean, val text: String?, val toggle: () -> Unit)

/** The step-away state of [pc]'s running session, or null with no PC to send it to. */
@Composable
fun rememberStepAway(pc: TodayPc, clock: ClockFormat): StepAwayState? {
    val app = LocalContext.current.applicationContext as CompanionApp
    val pending by app.actions.pending.collectAsState()
    val target = pc.pcId ?: return null
    val sending = pending.lastOrNull { it.target == target && (it.kind == SyncAction.STEP_AWAY || it.kind == SyncAction.BACK) }
    val away = when (sending?.kind) {
        SyncAction.STEP_AWAY -> true
        SyncAction.BACK -> false
        else -> pc.awaySince != null
    }
    val since = pc.awaySince?.let { clock.time(LocalDateTime.ofInstant(it, ZoneId.systemDefault())) }
    val text = when {
        sending != null -> if (away) "Stepping away · sending…" else "Back · sending…"
        away && since != null -> "Away from ${pc.name} since $since"
        else -> null
    }
    val context = LocalContext.current
    return StepAwayState(away, sending != null, text) {
        // The PC shown may have been removed since this screen was drawn: refuse, never send to a PC that is not here.
        val here = app.sync.received.value.any { it.file.device.deviceId == target && it.file.device.kind == SyncDeviceCard.KIND_PC }
        if (here) {
            app.actions.add(if (away) SyncAction.BACK else SyncAction.STEP_AWAY, target)
        } else {
            Toast.makeText(context, "That PC isn't linked to this phone any more.", Toast.LENGTH_LONG).show()
        }
    }
}
