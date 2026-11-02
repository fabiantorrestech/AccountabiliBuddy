package com.accountabilibuddy.companion.ui

import android.content.Intent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.placement.PlacementSession
import com.accountabilibuddy.companion.placement.WorkUseActivity
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Today, while a PC session runs (not on a break): "Need it for work" for the phone placement. Under way it says until
 * when and can end early. The same chooser as the parked notice's action.
 */
@Composable
fun WorkUseCard() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(Unit) {
        while (true) {
            now = Instant.now()
            kotlinx.coroutines.delay(5_000)
        }
    }
    val session = PlacementSession.current(app, now)?.takeUnless { it.onBreak } ?: return
    val active = app.workUse.active(now)
    val uses = app.workUse.usesIn(session.start)
    AbCard {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                if (active != null) {
                    val until = ClockFormat(is24Hour = false).time(LocalDateTime.ofInstant(active.end, ZoneId.systemDefault()))
                    Text("Work use until $until", style = Ab.type.body, color = Ab.colors.ink)
                } else {
                    Text("Need the phone for work?", style = Ab.type.body, color = Ab.colors.ink)
                    Text(
                        if (uses == 0) "A call, 2FA or a work app. Say what for and how long."
                        else "Used $uses ${if (uses == 1) "time" else "times"} in this session.",
                        style = Ab.type.small, color = Ab.colors.muted,
                    )
                }
            }
            if (active != null) {
                AbTonalButton("Done", onClick = {
                    app.workUse.endNow()
                    app.workUseChanged()
                    now = Instant.now()
                })
            } else {
                AbTonalButton("Need it for work", onClick = {
                    context.startActivity(Intent(context, WorkUseActivity::class.java))
                })
            }
        }
    }
}
