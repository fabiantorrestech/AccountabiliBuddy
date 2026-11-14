package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import com.accountabilibuddy.companion.guidance.GuidanceStore
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.shared.sync.LunchDiscardOutcome
import java.time.Duration
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.lunch.LunchStore
import com.accountabilibuddy.companion.ui.kit.AbValueButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * Settings → Your day → "Lunch length": how long a lunch started on this phone (or its watch) lasts, 15 minutes to
 * 2 hours in steps of 5. This phone's own setting; a flexible break's length in the day plan doesn't change it.
 */
@Composable
internal fun LunchSection(lengthMinutes: Int) {
    val lunch = (LocalContext.current.applicationContext as CompanionApp).lunch
    val length = LunchStore.formatLength(lengthMinutes)
    Section("Lunch length", summary = length) {
        Note("On any workday, Start lunch on Today or the watch takes this long; while it runs, +5 / −5 min change it.")
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            AbValueButton(
                "−5 min",
                onClick = { lunch.setLength(lengthMinutes - LunchStore.LENGTH_STEP) },
                enabled = lengthMinutes > LunchStore.MIN_LENGTH_MINUTES,
            )
            Text(length, style = Ab.type.timerSmall, color = Ab.colors.ink)
            AbValueButton(
                "+5 min",
                onClick = { lunch.setLength(lengthMinutes + LunchStore.LENGTH_STEP) },
                enabled = lengthMinutes < LunchStore.MAX_LENGTH_MINUTES,
            )
        }
        Note("The lunch's ✕ asks Count it (the minutes so far count, the rest stays for later today) or Discard it (as if lunch never started).")
        DiscardRow("AccountabiliBuddy mode", companion = false)
        DiscardRow("ProductaBuddy mode", companion = true)
    }
}

/**
 * "Allow discarding a lunch" for one mode (ADR 88): on unless turned off. Off applies at once; on waits the looser-change delay, is refused
 * during a focus session and has no "apply now". A waiting "on" shows with its time and a Cancel.
 */
@Composable
private fun DiscardRow(title: String, companion: Boolean) {
    val app = LocalContext.current.applicationContext as CompanionApp
    var message by remember { mutableStateOf<String?>(null) }
    // Drawing only reads the setting. A waiting "on" that waited long enough (and meets no session) is settled outside the drawing, in the
    // poll below (which also ticks the waiting line), never while this row is being composed.
    var setting by remember { mutableStateOf(app.lunch.discardSetting(companion)) }
    var trustedNow by remember { mutableStateOf(TrustedTime.now()) }
    PollWhileVisible({ if (setting.pending == true) 1_000L else 5_000L }) {
        app.lunchDiscardAllowed()
        setting = app.lunch.discardSetting(companion)
        trustedNow = TrustedTime.now()
    }
    ToggleRow(
        title, "Offers Discard it in this mode's \"End lunch?\" question, on the phone and the watch.", setting.allowed,
    ) { wanted ->
        val outcome = app.lunch.setDiscardAllowed(companion, wanted, TrustedTime.now(), GuidanceStore.DELAY, app.lunchSessionRunning())
        message = when (outcome) {
            LunchDiscardOutcome.APPLIED -> null
            LunchDiscardOutcome.WAITING -> "Turning on waits ${GuidanceStore.DELAY.toMinutes()} min. It happens on its own unless you cancel."
            LunchDiscardOutcome.REFUSED -> "Not during a focus session."
        }
        setting = app.lunch.discardSetting(companion)
    }
    val pendingFrom = setting.pendingFrom
    if (setting.pending == true && pendingFrom != null) {
        // Rounded up: "in about 10 min" right after asking for a 10-minute wait, 1 min for the last stretch.
        val minutes = Math.max(1L, (Duration.between(trustedNow, pendingFrom).seconds + 59) / 60)
        Note("Turns on in about $minutes min.")
        AbTextButton("Cancel this change", onClick = {
            app.lunch.setDiscardAllowed(companion, false, TrustedTime.now(), GuidanceStore.DELAY, app.lunchSessionRunning()); message = null; setting = app.lunch.discardSetting(companion)
        })
    }
    message?.let { Note(it) }
}
