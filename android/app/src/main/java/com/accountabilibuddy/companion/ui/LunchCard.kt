package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.lunch.LunchStore
import com.accountabilibuddy.companion.model.FixedLunch
import com.accountabilibuddy.companion.model.FlexibleBreak
import com.accountabilibuddy.shared.WatchLunch
import com.accountabilibuddy.shared.sync.SyncLunch
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Today's flexible lunch on the phone: on a workday (or a day with a flexible break) "Start lunch" any time for the
 * lunch length, then the time left, +5 / −5 min and "I'm back".
 */
@Composable
fun LunchCard(
    workday: Boolean,
    flex: FlexibleBreak?,
    fixed: FixedLunch?,
    taken: SyncLunch?,
    lengthMinutes: Int,
    clock: ClockFormat,
    onStart: () -> Unit,
    onBack: () -> Unit,
    onAdjust: (longer: Boolean) -> Unit,
) {
    if (!workday && flex == null && taken == null) return
    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(taken) {
        while (true) {
            now = Instant.now()
            kotlinx.coroutines.delay(1_000)
        }
    }
    val zone = ZoneId.systemDefault()
    fun time(at: Instant) = clock.time(LocalDateTime.ofInstant(at, zone))
    val onLunch = taken != null && now < taken.end
    val length = LunchStore.formatLength(lengthMinutes)
    Card(
        colors = CardDefaults.cardColors(containerColor = Color(0x33CA8A04)),
        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 6.dp),
    ) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Column(Modifier.weight(1f)) {
                    val text = when {
                        onLunch -> {
                            val left = Duration.between(now, taken.end).seconds.coerceAtLeast(0)
                            "Lunch · %d:%02d left · until %s".format(left / 60, left % 60, time(taken.end))
                        }
                        taken != null -> "Lunch done · ${time(taken.start)} – ${time(taken.end)}"
                        // A fixed lunch period: the PC starts it by itself (this phone doesn't ask about it).
                        // Only until its window ends: after that it can't start by itself any more.
                        fixed != null && LocalDateTime.ofInstant(now, zone).isBefore(fixed.window.end) ->
                            "Lunch starts at ${clock.time(fixed.window.start)} by itself · until ${clock.time(fixed.window.end)}"
                        flex != null -> "${flex.name} · $length · usually ${clock.time(flex.window.start)} – ${clock.time(flex.window.end)}"
                        else -> "Lunch · $length, any time"
                    }
                    IconTitle(text, style = MaterialTheme.typography.titleSmall, icon = SectionIcons.of("Lunch"))
                }
                when {
                    onLunch -> OutlinedButton(onClick = onBack) { Text("I'm back") }
                    taken == null -> Button(onClick = onStart) { Text("Start lunch") }
                }
            }
            if (onLunch) {
                val canShorten = taken.end.isAfter(now.plus(WatchLunch.SHORTEST_LEFT))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = { onAdjust(false) }, enabled = canShorten) { Text("−5 min") }
                    OutlinedButton(onClick = { onAdjust(true) }) { Text("+5 min") }
                }
            }
        }
    }
}
