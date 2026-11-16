package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.NightHandover
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * "Night routine starts in 5 minutes" with Snooze and, in ProductaBuddy only, Cancel (owner design 2026-11-15): shown on Today while the
 * night waits 5 minutes after a session on this phone ended. It looks at the night every 5 seconds on trusted time, so it needs no refresh.
 */
@Composable
fun NightHandoverCard() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    var pending by remember { mutableStateOf<NightHandover.Pending?>(null) }
    LaunchedEffect(Unit) {
        while (true) {
            pending = withContext(Dispatchers.Default) {
                val now = TrustedTime.now()
                NightHandover.check(context, now)
                NightHandover.pending(app, now)
            }
            // Every 5 seconds only while the card shows; a minute otherwise (battery).
            delay(if (pending != null) 5_000L else 60_000L)
        }
    }
    val shown = pending ?: return
    AbCard {
        Text("Night routine starts in 5 minutes", style = Ab.type.bodyMedium, color = Ab.colors.ink, fontWeight = FontWeight.SemiBold)
        Text("At ${CLOCK.format(NightHandover.local(shown.startAt))}.", style = Ab.type.body, color = Ab.colors.muted)
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically, modifier = Modifier) {
            if (shown.canSnooze) AbTonalButton("Snooze", onClick = { NightHandover.snooze(context) })
            if (shown.canCancel) AbTextButton("Cancel tonight", onClick = { NightHandover.cancel(context) })
        }
    }
}

private val CLOCK = DateTimeFormatter.ofPattern("h:mm a", Locale.US)
