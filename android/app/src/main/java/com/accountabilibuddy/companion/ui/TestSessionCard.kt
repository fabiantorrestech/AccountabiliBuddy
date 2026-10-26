package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
import com.accountabilibuddy.companion.session.TestSession
import java.time.Duration
import java.time.Instant

/**
 * Today → More options: the test session (1 minute by default, 1-60), to try the phone's settings (the essentials
 * screen, the LockMyPhone-Extended profile, Curbox hard mode, the notification) without a real session. Ends early
 * at once; can't start while a real session runs on another device.
 */
@Composable
fun TestSessionCard() {
    val app = LocalContext.current.applicationContext as CompanionApp
    val test = app.testSession
    val window by test.window.collectAsState()
    var minutes by remember { mutableStateOf(test.minutes) }
    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(window) {
        while (true) {
            now = Instant.now()
            kotlinx.coroutines.delay(1_000)
        }
    }
    val running = window?.takeIf { (start, end) -> !now.isBefore(start) && now.isBefore(end) }
    val realRunning = EssentialsLaunch.realSessionStart(app, now) != null
    val summary = when {
        running != null -> "Test session running"
        else -> "Test session · $minutes min"
    }

    Section("More options", summary = summary) {
        IconTitle("Test session", style = MaterialTheme.typography.titleSmall)
        Text(
            "Runs like a session on another device, on this phone only: never counted, not in History, not synced, not on the watch.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Hint("Try your settings: unlock the phone for the essentials screen, and see the LockMyPhone-Extended profile, Curbox hard mode and the notification react. Each follows its own switch in Settings. A real session on another device always comes first.")
        if (running != null) {
            val left = Duration.between(now, running.second).seconds.coerceAtLeast(0)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("Test session · %d:%02d left".format(left / 60, left % 60), style = MaterialTheme.typography.bodyLarge, modifier = androidx.compose.ui.Modifier.weight(1f))
                OutlinedButton(onClick = { test.endNow() }) { Text("End test") }
            }
            if (realRunning) {
                Text("A session on another device is running: it comes first.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        } else {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                TextButton(onClick = { minutes = shorter(minutes); test.minutes = minutes }, enabled = minutes > TestSession.MIN_MINUTES) { Text("−") }
                Text("$minutes min", style = MaterialTheme.typography.bodyLarge)
                TextButton(onClick = { minutes = longer(minutes); test.minutes = minutes }, enabled = minutes < TestSession.MAX_MINUTES) { Text("+") }
                androidx.compose.foundation.layout.Spacer(androidx.compose.ui.Modifier.weight(1f))
                Button(onClick = { test.start(minutes) }, enabled = !realRunning) { Text("Start test") }
            }
            if (realRunning) {
                Text("A session is running on another device: the test can start when it ends.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

// The stepper: by 1 minute up to 5, then by 5 (1-60).
private fun longer(minutes: Int): Int = (if (minutes < 5) minutes + 1 else minutes + 5).coerceAtMost(TestSession.MAX_MINUTES)

private fun shorter(minutes: Int): Int = (if (minutes <= 5) minutes - 1 else minutes - 5).coerceAtLeast(TestSession.MIN_MINUTES)
