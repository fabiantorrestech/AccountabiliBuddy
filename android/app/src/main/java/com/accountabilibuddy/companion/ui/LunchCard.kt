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
import com.accountabilibuddy.companion.model.FlexibleBreak
import com.accountabilibuddy.shared.sync.SyncLunch
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/** Today's flexible lunch on the phone: "Start lunch" any time, then the time left and "I'm back". */
@Composable
fun LunchCard(flex: FlexibleBreak?, taken: SyncLunch?, clock: ClockFormat, onStart: (Int) -> Unit, onBack: () -> Unit) {
    if (flex == null && taken == null) return
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
    Card(
        colors = CardDefaults.cardColors(containerColor = Color(0x33CA8A04)),
        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 6.dp),
    ) {
        Row(Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Column(Modifier.weight(1f)) {
                val text = when {
                    onLunch -> {
                        val left = Duration.between(now, taken!!.end).seconds.coerceAtLeast(0)
                        "Lunch · %d:%02d left · until %s".format(left / 60, left % 60, time(taken.end))
                    }
                    taken != null -> "Lunch done · ${time(taken.start)} – ${time(taken.end)}"
                    else -> "${flex!!.name} · any time ${clock.time(flex.window.start)} – ${clock.time(flex.window.end)} · ${flex.minutes} min"
                }
                IconTitle(text, style = MaterialTheme.typography.titleSmall, icon = SectionIcons.of("Lunch"))
            }
            when {
                onLunch -> OutlinedButton(onClick = onBack) { Text("I'm back") }
                taken == null && flex != null -> Button(onClick = { onStart(flex.minutes) }) { Text("Start lunch") }
            }
        }
    }
}
