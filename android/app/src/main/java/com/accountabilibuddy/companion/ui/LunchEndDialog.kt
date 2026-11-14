package com.accountabilibuddy.companion.ui

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * "End lunch?" (ADR 88), the question behind the lunch ✕ on Today and the pinned notification's "End lunch": Count it (the minutes so far count,
 * the rest stays for later), Discard it (as if lunch never started; only when the mode's "Allow discarding a lunch" is on) or Keep going. It reads
 * lunch fresh each time it shows, follows lunch while it is open (changes from another device, the minutes ticking) and closes itself when no lunch
 * runs any more (over, or ended on another device).
 */
@Composable
fun LunchEndDialog(open: Boolean, clock: ClockFormat, onClose: () -> Unit) {
    if (!open) return
    val app = LocalContext.current.applicationContext as CompanionApp
    val zone = ZoneId.systemDefault()
    // Follows lunch (a Count it or Discard it received from another device, a +5/-5) and the clock (the used minutes), so the question is never stale.
    val taken by app.lunch.lunch.collectAsStateWithLifecycle()
    val ticking by rememberTickingNow(taken) { 1_000L }
    // The mode's setting, settled outside the drawing (reading it can store a waiting "on" that came due).
    val discardAllowed by produceState(app.lunch.discardAllowed(app.guidance.isCompanion), taken) { value = app.lunchDiscardAllowed() }
    // [ticking] only makes this read again; the question itself is worked out on trusted time.
    val question = ticking.let { app.lunch.question(discardAllowed) { clock.time(LocalDateTime.ofInstant(it, zone)) } }
    // Nothing runs (a stale tap on the notification, or lunch ended under the question): nothing to ask.
    if (question == null) {
        LaunchedEffect(Unit) { onClose() }
        return
    }

    AbDialog(
        title = question.title,
        onDismiss = onClose,
        confirmText = "Count it",
        onConfirm = { app.lunch.count(TrustedTime.now()); onClose() },
        dismissText = "Keep going",
    ) {
        Text(question.usedText, style = Ab.type.bodyMedium, color = Ab.colors.muted)
        Text(question.countText, style = Ab.type.bodyMedium)
        question.discardText?.let { text ->
            Text(text, style = Ab.type.bodyMedium)
            AbTextButton("Discard it", onClick = { app.lunch.discard(TrustedTime.now(), app.lunchDiscardAllowed()); onClose() }, danger = true)
        }
    }
}
