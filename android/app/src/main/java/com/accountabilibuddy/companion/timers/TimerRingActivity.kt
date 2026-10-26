package com.accountabilibuddy.companion.timers

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.CompanionTheme

/**
 * The ring screen over the lock screen (like `BedtimeAlarmActivity`): every timer that's up and alarm that rings, each
 * with Dismiss and +1 min (a timer) or Snooze 9 min (an alarm). It closes when nothing rings any more.
 */
class TimerRingActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setShowWhenLocked(true)
        setTurnScreenOn(true)
        val store = (applicationContext as CompanionApp).timers
        setContent {
            CompanionTheme {
                val ringing by TimerRinger.ringing.collectAsStateWithLifecycle()
                LaunchedEffect(ringing.isEmpty()) { if (ringing.isEmpty()) finish() }
                Column(
                    Modifier.fillMaxSize().background(Color(0xFF0B1020)).padding(32.dp),
                    verticalArrangement = Arrangement.spacedBy(28.dp, Alignment.CenterVertically),
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    ringing.forEach { item ->
                        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
                            Text(item.text, color = Color(0xFF5EEAD4), fontSize = 18.sp)
                            Text(item.title, color = Color.White, fontSize = 30.sp, textAlign = TextAlign.Center)
                            Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                                if (item.kind == RingingItem.Kind.TIMER) {
                                    OutlinedButton(onClick = { store.addMinute(item.id) }) { Text("+1 min") }
                                    Button(onClick = { store.reset(item.id) }) { Text("Dismiss") }
                                } else {
                                    OutlinedButton(onClick = { store.snoozeAlarm(item.id) }) { Text("Snooze 9 min") }
                                    Button(onClick = { store.dismissAlarm(item.id) }) { Text("Dismiss") }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
