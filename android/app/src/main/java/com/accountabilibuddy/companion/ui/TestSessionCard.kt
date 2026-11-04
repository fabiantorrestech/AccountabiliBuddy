package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
import com.accountabilibuddy.companion.session.TestSession
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Duration
import java.time.Instant

/**
 * The test session (1 minute by default, 1-60), to try the phone's settings (the essentials screen, the LockMyPhone-Extended
 * profile, Curbox hard mode, the notification) without a real session. Ends early at once; can't start while a real session runs
 * on another device. On Today it is one of "More options" (a folding card); with [asPage] (Settings › Focus sessions) it is a card
 * of its own.
 */
@Composable
fun TestSessionCard(asPage: Boolean = false) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val test = app.testSession
    val window by test.window.collectAsState()
    var minutes by remember { mutableStateOf(test.minutes) }
    // 1 s only while a test runs; otherwise every 15 s (a session on another device that starts or ends).
    val now by rememberTickingNow(window) { read -> if (window?.let { (start, end) -> !read.isBefore(start) && read.isBefore(end) } == true) 1_000L else 15_000L }
    val running = window?.takeIf { (start, end) -> !now.isBefore(start) && now.isBefore(end) }
    val realRunning = EssentialsLaunch.realSessionStart(app, now) != null
    val summary = when {
        running != null -> "Test session running"
        else -> "Test session · $minutes min"
    }

    val body: @Composable () -> Unit = {
        if (!asPage) IconTitle("Test session", style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold)
        Note("Runs like a session on another device, on this phone only: never counted, not in History, not synced, not on the watch.")
        Hint("Try your settings: unlock the phone for the essentials screen, and see the LockMyPhone-Extended profile, Curbox hard mode and the notification react. Each follows its own switch in Settings. A real session on another device always comes first.")
        if (running != null) {
            val left = Duration.between(now, running.second).seconds.coerceAtLeast(0)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("Test session · %d:%02d left".format(left / 60, left % 60), style = Ab.type.body, color = Ab.colors.ink, modifier = Modifier.weight(1f))
                AbTonalButton("End test", onClick = { test.endNow() })
            }
            if (realRunning) {
                Note("A session on another device is running: it comes first.")
            }
        } else {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                AbTextButton("−", onClick = { minutes = shorter(minutes); test.minutes = minutes }, enabled = minutes > TestSession.MIN_MINUTES)
                Text("$minutes min", style = Ab.type.body, color = Ab.colors.ink)
                AbTextButton("+", onClick = { minutes = longer(minutes); test.minutes = minutes }, enabled = minutes < TestSession.MAX_MINUTES)
                Spacer(Modifier.weight(1f))
                AbTonalButton("Start test", onClick = { test.start(minutes) }, enabled = !realRunning)
            }
            if (realRunning) {
                Note("A session is running on another device: the test can start when it ends.")
            }
        }
    }

    if (asPage) {
        AbCard(verticalSpacing = 8.dp) {
            IconTitle("Test session", style = Ab.type.section, fontWeight = FontWeight.SemiBold)
            body()
        }
    } else {
        Section("More options", summary = summary) { body() }
    }
}

// The stepper: by 1 minute up to 5, then by 5 (1-60).
private fun longer(minutes: Int): Int = (if (minutes < 5) minutes + 1 else minutes + 5).coerceAtMost(TestSession.MAX_MINUTES)

private fun shorter(minutes: Int): Int = (if (minutes <= 5) minutes - 1 else minutes - 5).coerceAtLeast(TestSession.MIN_MINUTES)
