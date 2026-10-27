package com.accountabilibuddy.companion.nudge

import android.content.Intent
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
    // Lunch (from any device) is a break: an open card closes, as the PC's does.
    private val lunchCheck: Runnable = object : Runnable {
        override fun run() {
            if ((application as com.accountabilibuddy.companion.CompanionApp).lunch.isOnLunch(java.time.Instant.now())) {
                finish()
                return
            }

            window.decorView.removeCallbacks(this)
            window.decorView.postDelayed(this, 5_000L)
        }
    }

    override fun onResume() {
        super.onResume()
        lunchCheck.run()
    }

    override fun onPause() {
        window.decorView.removeCallbacks(lunchCheck)
        super.onPause()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() = Unit
        })
        val app = application as CompanionApp
        val store = NudgeStore(this)
        val untilText = intent.getStringExtra(EXTRA_UNTIL)
        val parked = intent.getStringArrayListExtra(EXTRA_PARKED).orEmpty()
        val bonus = intent.getIntExtra(EXTRA_BONUS_GOAL, 0).takeIf { it > 0 }?.let { goal ->
            GoalBonus(goal, intent.getIntExtra(EXTRA_BONUS_FOCUSED, goal), intent.getIntExtra(EXTRA_BONUS_STREAK, 1))
        }
        setContent {
            CompanionTheme {
                NudgeCard(store, app.emergency, untilText, parked, bonus, onEssentials = {
                    startActivity(com.accountabilibuddy.companion.essentials.EssentialsActivity.intent(this))
                }, onDone = {
                    store.update { it.copy(unanswered = false) }
                    finish()
                    app.refreshSoon()
                })
            }
        }
    }

    // Home (or anything else that hides the card) leaves it without an answer: it stays flagged in the store, and a
    // refresh in 5 minutes shows it again (the refresh also runs on its normal schedule).
    override fun onStop() {
        super.onStop()
        if (isFinishing) return
        val alarms = getSystemService(android.app.AlarmManager::class.java)
        val refresh = android.app.PendingIntent.getBroadcast(
            this, REFRESH_REQUEST, Intent(this, com.accountabilibuddy.companion.notify.RefreshReceiver::class.java),
            android.app.PendingIntent.FLAG_IMMUTABLE or android.app.PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val at = System.currentTimeMillis() + NudgeRules.SNOOZE.toMillis()
        runCatching { alarms.setAndAllowWhileIdle(android.app.AlarmManager.RTC_WAKEUP, at, refresh) }
    }

    companion object {
        private const val REFRESH_REQUEST = 41
        const val EXTRA_UNTIL = "until"
        const val EXTRA_PARKED = "parked"
        const val EXTRA_BONUS_FOCUSED = "bonusFocused"
        const val EXTRA_BONUS_GOAL = "bonusGoal"
        const val EXTRA_BONUS_STREAK = "bonusStreak"
    }
}

@Composable
private fun NudgeCard(
    store: NudgeStore, emergency: EmergencyStore, untilText: String?, parked: List<String>, bonus: GoalBonus?, onDone: () -> Unit,
    onEssentials: () -> Unit,
) {
    // "Not working today" opens a choice: a day off, or the phone for a bit (emergency).
    var menu by remember { mutableStateOf<String?>(null) }
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
            if (menu == MENU_EMERGENCY) {
                EmergencyChooser(emergency, onStarted = { onDone(); onEssentials() }, onBack = { menu = MENU_CHOOSE })
                return@Column
            }

            if (bonus != null) {
                // Goal met: framed as a bonus round, the extra counted up.
                Text("Goal met ✓ · ${GoalBonus.duration(bonus.focusedMinutes)} today", color = Color(0xFF22C55E), fontSize = 18.sp)
                Text("Bonus round?", color = Color.White, fontSize = 32.sp, lineHeight = 38.sp)
                Text(
                    listOfNotNull(
                        if (bonus.bonusMinutes > 0) "+${GoalBonus.duration(bonus.bonusMinutes)} over your ${GoalBonus.duration(bonus.goalMinutes)} goal" else "Right on your ${GoalBonus.duration(bonus.goalMinutes)} goal",
                        if (bonus.streakDays > 1) "${bonus.streakDays}-day streak" else null,
                        untilText,
                    ).joinToString(" · "),
                    color = Color(0xCCFFFFFF), style = MaterialTheme.typography.bodyLarge,
                )
            } else {
                Text("Work time", color = Color(0xFF7DD3C0), fontSize = 18.sp)
                Text("Plan 3 tasks and start", color = Color.White, fontSize = 32.sp, lineHeight = 38.sp)
                Text(
                    listOfNotNull(untilText, "No session is running on any of your devices.").joinToString(" · "),
                    color = Color(0xCCFFFFFF), style = MaterialTheme.typography.bodyLarge,
                )
            }
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

            if (menu == MENU_CHOOSE) {
                Text("Not working today?", color = Color.White, style = MaterialTheme.typography.titleLarge)
                Button(onClick = { menu = MENU_EMERGENCY }, modifier = Modifier.fillMaxWidth()) { Text("I need my phone for a bit") }
                OutlinedButton(onClick = { menu = null; save(state.copy(dayOffAskedAt = now)) }, modifier = Modifier.fillMaxWidth()) {
                    Text("A day off (10-minute wait)", color = Color.White)
                }
                TextButton(onClick = { menu = null }, modifier = Modifier.fillMaxWidth()) { Text("Back", color = Color(0x99FFFFFF)) }
                return@Column
            }

            if (bonus != null) {
                Button(onClick = { save(NudgeRules.starting(state, now)); onDone() }, modifier = Modifier.fillMaxWidth()) {
                    Text("Start a bonus block", fontSize = 18.sp)
                }
                // After the goal, "Not now" is free: no count, no reason.
                OutlinedButton(onClick = { save(state.copy(snoozedUntil = now.plusSeconds(GoalBonus.REPEAT_MINUTES * 60L))); onDone() }, modifier = Modifier.fillMaxWidth()) {
                    Text("Not now", color = Color.White)
                }
                OutlinedButton(onClick = { onDone(); onEssentials() }, modifier = Modifier.fillMaxWidth()) {
                    Text("Essentials only", color = Color.White)
                }
                TextButton(onClick = { menu = MENU_CHOOSE }, modifier = Modifier.fillMaxWidth()) {
                    Text("Not working today · need my phone", color = Color(0x99FFFFFF))
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
            // Just the essentials (calls, messages, maps…), without leaving work time behind.
            OutlinedButton(onClick = { onDone(); onEssentials() }, modifier = Modifier.fillMaxWidth()) {
                Text("Essentials only", color = Color.White)
            }
            TextButton(onClick = { menu = MENU_CHOOSE }, modifier = Modifier.fillMaxWidth()) {
                Text("Not working today · need my phone", color = Color(0x99FFFFFF))
            }
        }
    }
}

private const val MENU_CHOOSE = "choose"
private const val MENU_EMERGENCY = "emergency"
