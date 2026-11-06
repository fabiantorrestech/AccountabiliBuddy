package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.HourglassTop
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.notify.RunningWaits
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlinx.coroutines.delay
import java.time.Duration
import java.time.Instant

/**
 * Today's lines for the waits running in the background (ADR 66): the phone's own armed end of a PC session and each PC's armed
 * ends, each counting down with Cancel. Nothing when none runs. Ticks once a second only while one shows.
 */
@Composable
fun RunningWaitLines() {
    val app = LocalContext.current.applicationContext as CompanionApp
    var now by remember { mutableStateOf(Instant.now()) }
    var waits by remember { mutableStateOf(RunningWaits.current(app, now)) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(if (waits.isEmpty()) 5_000L else 1_000L)
            now = Instant.now()
            waits = RunningWaits.current(app, now)
        }
    }
    if (waits.isEmpty()) {
        return
    }

    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        waits.forEach { wait ->
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Icon(Icons.Outlined.HourglassTop, contentDescription = null, tint = Ab.meaning(Ab.colors.waiting), modifier = Modifier.size(20.dp))
                Text("${wait.label} in ${countdown(Duration.between(now, wait.readyAt))}", style = Ab.type.bodyMedium, color = Ab.colors.ink, modifier = Modifier.weight(1f))
                if (wait.cancelling) {
                    Text("Cancelling…", style = Ab.type.small, color = Ab.colors.ink)
                } else {
                    TextButton(onClick = {
                        RunningWaits.cancel(app, wait.key)
                        waits = RunningWaits.current(app)
                    }) { Text("Cancel") }
                }
            }
        }
    }
}

// "0:42" under an hour, else "1:05:00".
private fun countdown(left: Duration): String {
    val seconds = left.seconds.coerceAtLeast(0)
    return if (seconds >= 3600) "%d:%02d:%02d".format(seconds / 3600, seconds / 60 % 60, seconds % 60) else "%d:%02d".format(seconds / 60, seconds % 60)
}
