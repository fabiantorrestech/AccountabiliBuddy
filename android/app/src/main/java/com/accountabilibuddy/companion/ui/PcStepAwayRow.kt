package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.shared.sync.SyncAction
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * While a PC's session runs (from its sync file, ADR 26): Step away or I'm back from the phone, e.g. when leaving
 * the desk with the phone. The PC applies it within 10 minutes of the tap, or not at all.
 */
@Composable
internal fun PcStepAwayRow(clock: ClockFormat) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val received by app.sync.received.collectAsState()
    val pending by app.actions.pending.collectAsState()
    val now = Instant.now()
    val pc = received
        .filter { it.file.device.kind == SyncDeviceCard.KIND_PC && it.file.running != null }
        .filter { Duration.between(it.file.device.writtenAt, now) <= Duration.ofHours(12) }
        .filter { device -> device.file.running!!.phaseEndsAt?.let { now.isBefore(it.plusSeconds(600)) } ?: true }
        .maxByOrNull { it.file.device.writtenAt }
        ?: return
    val target = pc.file.device.deviceId
    val awaySince = pc.file.running!!.awaySince
    val sending = pending.lastOrNull { it.target == target && (it.kind == SyncAction.STEP_AWAY || it.kind == SyncAction.BACK) }
    val away = when (sending?.kind) {
        SyncAction.STEP_AWAY -> true
        SyncAction.BACK -> false
        else -> awaySince != null
    }
    val text = when {
        sending != null -> if (away) "Stepping away on ${pc.file.device.name} · sending…" else "Back on ${pc.file.device.name} · sending…"
        away && awaySince != null -> "Away from ${pc.file.device.name} since ${clock.time(LocalDateTime.ofInstant(awaySince, ZoneId.systemDefault()))}"
        else -> "Session on ${pc.file.device.name}"
    }
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        modifier = Modifier.fillMaxWidth().background(Color(0x1A64748B), RoundedCornerShape(12.dp)).padding(horizontal = 12.dp, vertical = 6.dp),
    ) {
        Text(text, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
        OutlinedButton(enabled = sending == null, onClick = {
            app.actions.add(if (away) SyncAction.BACK else SyncAction.STEP_AWAY, target)
        }) { Text(if (away) "I'm back" else "Step away") }
    }
}
