package com.accountabilibuddy.companion.ui

import android.os.SystemClock
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.guidance.AppBrand
import com.accountabilibuddy.companion.guidance.GuidanceMode
import com.accountabilibuddy.companion.guidance.SessionEndCost
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbChoiceRow
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlinx.coroutines.delay
import java.time.Duration

/**
 * "How AB helps" (ADR 68): AccountabiliBuddy (guards you) or ProductaBuddy (companion only), one choice for every linked device. On
 * Settings › Guards, in the first-run setup and in What's new. ProductaBuddy waits 10 minutes. During a focus session it asks: after
 * the session ends, or now at what ending it early costs (its wait and a reason), then the 10 minutes with the session still running.
 * Back to AccountabiliBuddy is at once.
 */
@Composable
fun GuidanceSection(inSettings: Boolean = false) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val state by app.guidance.state.collectAsState()
    val pending = state.pending
    // Ticks the wait down while it runs.
    val now by rememberTickingNow(pending) { if (pending != null) 1_000L else null }
    var duringSession by remember { mutableStateOf<SessionEndCost?>(null) }
    var payingNow by remember { mutableStateOf<SessionEndCost?>(null) }
    val companionShown = state.mode == GuidanceMode.Companion || pending != null
    fun chooseCompanion() {
        if (!companionShown) {
            // During a focus session it asks when: after it ends, or now at what ending it early costs.
            val cost = SessionEndCost.now(app)
            if (cost == null) app.guidance.requestCompanion() else duringSession = cost
        } else if (pending?.duringSession == false) {
            // Waiting for the session to end: "now" can still be paid for.
            SessionEndCost.now(app)?.let { duringSession = it }
        }
    }
    AbCard(contentPadding = PaddingValues(horizontal = 16.dp, vertical = 14.dp), verticalSpacing = 8.dp) {
        if (inSettings) {
            // Settings › App & data › App mode: one switch, on = ProductaBuddy (on its way counts as on).
            Text("App mode, on all your devices", style = Ab.type.body, fontWeight = FontWeight.SemiBold)
            ToggleRow(
                "Use ${AppBrand.COMPANION}",
                if (companionShown) "Sessions (and their locks), your day, tasks, stats, steps, sounds and Curbox's blocks. Nothing else nudges or locks."
                else "Off: ${AppBrand.PRODUCT} also brings the work card, the work-start nudge, Pre-Work, the parked phone, bedtime reminders and the night's lock.",
                companionShown,
            ) { on -> if (on) chooseCompanion() else app.guidance.chooseGuarding() }
        } else {
            Text("How AB helps, on all your devices", style = Ab.type.body, fontWeight = FontWeight.SemiBold)
            AbChoiceRow(
                "${AppBrand.PRODUCT}: guards you",
                selected = !companionShown,
                subtitle = "The work card, the work-start nudge, Pre-Work, the parked phone, bedtime reminders and the night's lock.",
            ) {
                if (companionShown) app.guidance.chooseGuarding()
            }
            AbChoiceRow(
                "${AppBrand.COMPANION}: companion only",
                selected = companionShown,
                subtitle = "Sessions (and their locks), your day, tasks, stats, steps, sounds and Curbox's blocks. Nothing else nudges or locks.",
                onClick = { chooseCompanion() },
            )
        }
        if (inSettings && pending?.duringSession == false && SessionEndCost.now(app) != null) {
            AbTextButton("Switch now instead…", onClick = { SessionEndCost.now(app)?.let { duringSession = it } })
        }
        if (pending != null) {
            // The countdown counts with the same trusted time the wait does (ADR 70); `now` only makes it tick.
            val trustedNow = now.let { com.accountabilibuddy.companion.guidance.TrustedTime.now() }
            val sessionRunning = SessionEndCost.now(app) != null
            Note(
                when {
                    pending.duringSession ->
                        "${AppBrand.COMPANION} in ${left(Duration.between(trustedNow, pending.applyAt))}, with the session still running. Until then everything still guards you."
                    sessionRunning && !trustedNow.isBefore(pending.applyAt) ->
                        "${AppBrand.COMPANION} when this session ends. Until then everything still guards you."
                    else ->
                        "${AppBrand.COMPANION} in ${left(Duration.between(trustedNow, pending.applyAt))}, or after a session running then. Until then everything still guards you."
                },
            )
            AbTextButton("Cancel, keep guarding", onClick = { app.guidance.chooseGuarding() })
        }
        if (!state.isChosen && pending == null) {
            Note("This is this phone's starting choice: left as it is, it follows your linked devices. Keep makes it your choice, and your other devices follow it.")
            AbTextButton("Keep ${AppBrand.of(state.mode)}", onClick = { app.guidance.confirmDefault() })
        }
        if (state.mode == GuidanceMode.Companion) Note("${AppBrand.PRODUCT}-only settings are hidden while ${AppBrand.COMPANION} is in effect.")
        Note(
            "During a focus session, ${AppBrand.COMPANION} asks: after it ends, or now at what ending it early costs. Your other devices " +
                "follow after their own 10-minute wait; going back to ${AppBrand.PRODUCT} reaches them at once.",
        )
    }
    // ADR 69: where the two modes' own settings differ, editable here.
    if (inSettings) ModeDifferences(state.mode)

    duringSession?.let { cost ->
        DuringSessionDialog(
            cost,
            onAfter = {
                duringSession = null
                app.guidance.requestCompanionAfterSession()
            },
            onNow = {
                duringSession = null
                if (cost.free) app.guidance.requestCompanionNow("") else payingNow = cost
            },
            onCancel = { duringSession = null },
        )
    }
    payingNow?.let { cost ->
        NowCostDialog(
            cost,
            onConfirm = { reason ->
                payingNow = null
                app.guidance.requestCompanionNow(reason)
            },
            onCancel = { payingNow = null },
        )
    }
}

/** After this session ends, or now at what ending it early costs (ADR 68). */
@Composable
private fun DuringSessionDialog(cost: SessionEndCost, onAfter: () -> Unit, onNow: () -> Unit, onCancel: () -> Unit) {
    var nowChosen by remember { mutableStateOf(false) }
    val nowCost = if (cost.free) "nothing extra" else "a ${cost.waitSeconds} s wait and a reason"
    AlertDialog(
        onDismissRequest = onCancel,
        title = { Text("Switch to ${AppBrand.COMPANION} during this session?") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Note("The session keeps its locks either way; what changes is that nothing else nudges or locks.")
                AbChoiceRow("After this session ends (10 minutes from now at the earliest)", selected = !nowChosen) { nowChosen = false }
                if (!cost.hasSetEnd) {
                    Note(
                        "This session has no set end: “after this session ends” means ${AppBrand.COMPANION} takes effect when you end it yourself.",
                        color = Ab.colors.waiting,
                    )
                }
                AbChoiceRow("Now: costs what ending this session early costs ($nowCost), then 10 minutes", selected = nowChosen) { nowChosen = true }
            }
        },
        confirmButton = { TextButton(onClick = { if (nowChosen) onNow() else onAfter() }) { Text("Proceed") } },
        dismissButton = { TextButton(onClick = onCancel) { Text("Cancel") } },
    )
}

/** "Now": the end-early wait counts down here and the reason is typed, as ending the session early takes. */
@Composable
private fun NowCostDialog(cost: SessionEndCost, onConfirm: (String) -> Unit, onCancel: () -> Unit) {
    val openedAt = remember { SystemClock.elapsedRealtime() }
    fun secondsLeft(): Int = (cost.waitSeconds - ((SystemClock.elapsedRealtime() - openedAt) / 1000).toInt()).coerceAtLeast(0)
    var left by remember { mutableIntStateOf(secondsLeft()) }
    var reason by remember { mutableStateOf("") }
    LaunchedEffect(Unit) {
        while (left > 0) {
            delay(1_000)
            left = secondsLeft()
        }
    }
    val reasonOk = reason.trim().length >= cost.minReasonLength
    AlertDialog(
        onDismissRequest = onCancel,
        title = { Text("Switch to ${AppBrand.COMPANION} now?") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(
                    if (left > 0) {
                        "Take a breath. You can switch in ${left / 60}:${"%02d".format(left % 60)}; ${AppBrand.COMPANION} then takes effect after 10 minutes."
                    } else {
                        "You can switch now. ${AppBrand.COMPANION} takes effect after 10 minutes, with this session still running."
                    },
                )
                OutlinedTextField(
                    value = reason,
                    onValueChange = { reason = it },
                    label = { Text("Why switch now? (at least ${cost.minReasonLength} characters)") },
                )
            }
        },
        confirmButton = { TextButton(onClick = { onConfirm(reason.trim()) }, enabled = left == 0 && reasonOk) { Text("Switch") } },
        dismissButton = { TextButton(onClick = onCancel) { Text("Keep guarding") } },
    )
}

/**
 * The differences between the modes (ADR 69), under the App mode switch: the settings each mode keeps for itself, only where they
 * differ. Make one side win for all of them, or pick a side per row and apply; both modes then keep that value. Hidden when nothing
 * differs.
 */
@Composable
private fun ModeDifferences(mode: GuidanceMode) {
    val app = LocalContext.current.applicationContext as CompanionApp
    var version by remember { mutableStateOf(0) }
    val rows = remember(mode, version) { app.modeSettings.compare(mode) }
    // Per row: null = leave it, true = AccountabiliBuddy's value, false = ProductaBuddy's.
    val picks = remember(rows) { androidx.compose.runtime.mutableStateMapOf<String, Boolean>() }
    fun apply(chosen: List<Pair<com.accountabilibuddy.companion.guidance.PhoneModeSettings.Row, Boolean>>) {
        if (chosen.isEmpty()) return
        app.modeSettings.apply(mode, chosen)
        version++
    }
    if (rows.isEmpty()) return
    AbCard(contentPadding = PaddingValues(horizontal = 16.dp, vertical = 14.dp), verticalSpacing = 8.dp) {
        Text("Differences between the modes", style = Ab.type.body, fontWeight = FontWeight.SemiBold)
        Note("Each mode keeps these settings for itself. Pick the value both modes should keep, or make one side win for all of them.")
        rows.forEach { row ->
            Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text("${row.setting.label} · ${row.setting.section}", style = Ab.type.body, fontWeight = FontWeight.SemiBold)
                AbChoiceRow("${AppBrand.PRODUCT}: ${row.setting.format(row.guarding)}", selected = picks[row.setting.id] == true) {
                    picks[row.setting.id] = true
                }
                AbChoiceRow("${AppBrand.COMPANION}: ${row.setting.format(row.companion)}", selected = picks[row.setting.id] == false) {
                    picks[row.setting.id] = false
                }
            }
        }
        AbTextButton("Apply picks", enabled = picks.isNotEmpty(), onClick = { apply(rows.mapNotNull { row -> picks[row.setting.id]?.let { row to it } }) })
        AbTextButton("Sync ${AppBrand.PRODUCT} → ${AppBrand.COMPANION}", onClick = { apply(rows.map { it to true }) })
        AbTextButton("Sync ${AppBrand.COMPANION} → ${AppBrand.PRODUCT}", onClick = { apply(rows.map { it to false }) })
    }
}

/** "9:42" under an hour, else "1 h 05 m". */
private fun left(span: Duration): String {
    val seconds = span.seconds.coerceAtLeast(0)
    return if (seconds < 3600) "%d:%02d".format(seconds / 60, seconds % 60) else "${seconds / 3600} h %02d m".format(seconds % 3600 / 60)
}
