package com.accountabilibuddy.companion.nudge

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.OnBackPressedCallback
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.CompanionTheme
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId

/**
 * The full-screen work nudge: during work time with no session, unlocking the phone shows this card over whatever was
 * open. "I'm starting on my PC" gives 10 minutes to start; "Snooze 5 min" is free twice a day, then needs a reason;
 * "Not working today" waits 10 minutes, then no more nudges today on the phone. Back does nothing: one of them it is.
 */
class NudgeActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() = Unit
        })
        val app = application as CompanionApp
        val store = NudgeStore(this)
        val untilText = intent.getStringExtra(EXTRA_UNTIL)
        val parked = intent.getStringArrayListExtra(EXTRA_PARKED).orEmpty()
        setContent {
            CompanionTheme {
                NudgeCard(store, untilText, parked, onDone = {
                    finish()
                    app.refreshSoon()
                })
            }
        }
    }

    companion object {
        const val EXTRA_UNTIL = "until"
        const val EXTRA_PARKED = "parked"
    }
}

@Composable
private fun NudgeCard(store: NudgeStore, untilText: String?, parked: List<String>, onDone: () -> Unit) {
    val zone = ZoneId.systemDefault()
    var state by remember { mutableStateOf(store.load()) }
    var now by remember { mutableStateOf(Instant.now()) }
    var reason by remember { mutableStateOf("") }
    var askingReason by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) {
        while (true) {
            now = Instant.now()
            kotlinx.coroutines.delay(1_000)
        }
    }
    val today = LocalDate.now(zone)
    val dayOffLeft = NudgeRules.dayOffLeft(state, now, today, zone)
    fun save(next: NudgeState) {
        state = next
        store.save(next)
    }

    Box(Modifier.fillMaxSize().background(Color(0xFF0B1F1E)), contentAlignment = Alignment.Center) {
        Column(Modifier.fillMaxWidth().padding(28.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Text("Work time", color = Color(0xFF7DD3C0), fontSize = 18.sp)
            Text("Plan 3 tasks and start", color = Color.White, fontSize = 32.sp, lineHeight = 38.sp)
            Text(
                listOfNotNull(untilText, "No session is running on any of your devices.").joinToString(" · "),
                color = Color(0xCCFFFFFF), style = MaterialTheme.typography.bodyLarge,
            )
            if (parked.isNotEmpty()) {
                Text("Parked for later:", color = Color(0x99FFFFFF), style = MaterialTheme.typography.bodyMedium)
                parked.take(3).forEach { Text("• $it", color = Color(0xCCFFFFFF), style = MaterialTheme.typography.bodyMedium) }
            }

            if (dayOffLeft != null) {
                val seconds = dayOffLeft.seconds
                Text(
                    if (seconds > 0) "Not working today: no nudges in %d:%02d. Plan your day meanwhile.".format(seconds / 60, seconds % 60)
                    else "Done. Enjoy your day off.",
                    color = Color(0xFFF4A261), style = MaterialTheme.typography.bodyLarge,
                )
                if (seconds <= 0) {
                    Button(onClick = { save(state.copy(dayOff = today)); onDone() }, modifier = Modifier.fillMaxWidth()) { Text("Close") }
                }
                // Tapped by mistake: back to the card's choices (being stricter needs no wait).
                OutlinedButton(onClick = { save(state.copy(dayOffAskedAt = null, dayOff = null)) }, modifier = Modifier.fillMaxWidth()) {
                    Text("Cancel: I am working today")
                }
                return@Column
            }

            Button(onClick = { save(NudgeRules.starting(state, now)); onDone() }, modifier = Modifier.fillMaxWidth()) {
                Text("I'm starting on my PC", fontSize = 18.sp)
            }

            val needsReason = NudgeRules.snoozeNeedsReason(state, today)
            val left = (NudgeRules.FREE_SNOOZES - NudgeRules.snoozesToday(state, today)).coerceAtLeast(0)
            if (askingReason) {
                OutlinedTextField(
                    value = reason, onValueChange = { reason = it },
                    label = { Text("Why snooze again? (${NudgeRules.REASON_LENGTH}+ characters)") },
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            OutlinedButton(
                onClick = {
                    when {
                        !needsReason -> { save(NudgeRules.snooze(state, now, today)); onDone() }
                        !askingReason -> askingReason = true
                        reason.trim().length >= NudgeRules.REASON_LENGTH -> { save(NudgeRules.snooze(state, now, today)); onDone() }
                    }
                },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(if (needsReason) "Snooze 5 min (needs a reason)" else "Snooze 5 min ($left free left today)", color = Color.White)
            }
            TextButton(onClick = { save(state.copy(dayOffAskedAt = now)) }, modifier = Modifier.fillMaxWidth()) {
                Text("Not working today (10-minute wait)", color = Color(0x99FFFFFF))
            }
        }
    }
}
