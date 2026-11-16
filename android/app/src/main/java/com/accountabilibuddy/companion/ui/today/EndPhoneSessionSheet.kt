package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.HourglassTop
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
import com.accountabilibuddy.companion.essentials.LockMyPhoneInstaller
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbRing
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.rememberTickingNow
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.session.EndSheetAction
import com.accountabilibuddy.shared.session.EndSheetButton
import com.accountabilibuddy.shared.session.PhoneEndSheet
import com.accountabilibuddy.shared.session.PhoneSessionEndRules
import com.accountabilibuddy.shared.session.PhoneSessionResult
import kotlinx.coroutines.launch
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * End for a session on this phone (PS3a, plan R8, R9; no board: built from [AbSheet] and the R2b sheets' parts). Everything it says and every button is
 * [PhoneEndSheet] (pure, checked); this only draws it and makes each button one controller call.
 *
 * AccountabiliBuddy, a timed session: the wait counts from opening the sheet ([com.accountabilibuddy.companion.session.PhoneSessionController.openEnd]); the reason
 * can be typed meanwhile; "Confirm · ends in 0:42" arms the end, which applies at the wait's end whatever happens to the screen (an exact alarm); "End session" once
 * the wait is over; Cancel is free. ProductaBuddy and a session with no set end: "End now", nothing asked. "All tasks done · End now" when every task of the
 * checklist was ticked. The mode is read live, so a switch applies from then. The reason is private: it lives only in this sheet's memory and the sealed store.
 */
@Composable
fun EndPhoneSessionSheet(clock: ClockFormat, onMessage: (String) -> Unit, onDismiss: () -> Unit) {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val controller = app.phoneSession.controller
    val record by app.phoneSession.running.collectAsState()
    val now by rememberTickingNow()
    val scope = rememberCoroutineScope()
    var reason by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    var installMessage by remember { mutableStateOf<String?>(null) }

    val shown = record?.takeIf { it.isRunning && !it.isTest }
    val mode = EssentialsLaunch.mode(app)
    val terms = shown?.let { PhoneSessionEndRules.terms(mode, it, app.phoneSession.endSettings()) }

    // The wait counts from opening the sheet; opening again keeps the first time. Nothing to count for an End that costs nothing.
    LaunchedEffect(shown?.id, terms?.isNone) {
        if (shown != null && terms != null && !terms.isNone) controller.openEnd()
    }
    // The session ended (the end applied, All tasks done, End now): the sheet is done.
    LaunchedEffect(shown == null) { if (shown == null) onDismiss() }
    // A swipe down or Back with an End in progress that was not confirmed drops it (free): the wait starts again next time. A confirmed (armed) end stays.
    DisposableEffect(Unit) {
        onDispose { if (controller.running?.armedEnd == null) controller.cancelEnd() }
    }
    if (shown == null) return

    val zone = ZoneId.systemDefault()
    val time = { at: Instant -> clock.time(LocalDateTime.ofInstant(at, zone)) }
    val lmpUntil = remember(shown.id, shown.armedEnd) { EssentialsLaunch.lockOutlivesSession(context, shown, controller.now()) }
    val sheet = PhoneEndSheet.state(shown, mode, app.phoneSession.endSettings(), now, reason, lmpUntil, time)

    fun press(button: EndSheetButton) {
        if (busy) return
        val result: PhoneSessionResult = when (button.action) {
            EndSheetAction.END_NOW -> controller.endEarly(null)
            EndSheetAction.CONFIRM, EndSheetAction.END -> controller.endEarly(reason)
            EndSheetAction.ALL_DONE -> controller.endAtOnce()
            EndSheetAction.CANCEL -> {
                controller.cancelEnd()
                onDismiss()
                return
            }
        }
        refusalText(result)?.let(onMessage)
        // A confirmed end during the wait stays open to show "ends at …" and its Cancel; an end that happened closes the sheet by itself (above).
    }

    AbSheet(
        title = sheet.title,
        subtitle = shown.profileName,
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        aboveKeyboard = true,
        footer = {
            Column(Modifier.fillMaxWidth().padding(horizontal = 24.dp, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                sheet.allDone?.let { AbPrimaryButton(it.text, onClick = { press(it) }, enabled = it.enabled, modifier = Modifier.fillMaxWidth()) }
                sheet.primary?.let {
                    // With "All tasks done" offered above, the paid way down is the quieter button.
                    if (sheet.allDone != null) AbTonalButton(it.text, onClick = { press(it) }, enabled = it.enabled, modifier = Modifier.fillMaxWidth())
                    else AbPrimaryButton(it.text, onClick = { press(it) }, enabled = it.enabled, modifier = Modifier.fillMaxWidth())
                }
                AbTonalButton(sheet.cancel.text, onClick = { press(sheet.cancel) }, modifier = Modifier.fillMaxWidth())
            }
        },
    ) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 24.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Text(sheet.body, style = Ab.type.bodyMedium, color = Ab.colors.muted)
            sheet.armedLine?.let { line ->
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    Icon(Icons.Outlined.HourglassTop, contentDescription = null, tint = Ab.meaning(Ab.colors.waiting), modifier = Modifier.size(22.dp))
                    Text(line, style = Ab.type.bodyMedium, color = Ab.colors.ink, modifier = Modifier.weight(1f))
                }
            }
            if (sheet.asksReason && sheet.armedLine == null) {
                WaitRing(sheet.waitSeconds, sheet.waitLeftSeconds)
                // Typed meanwhile: only its length is read, here and in the engine; the text is sealed with the end and never logged or synced.
                AbTextField(
                    value = reason, onValueChange = { reason = it }, label = "Why? (at least ${sheet.minReason} characters)",
                    singleLine = false, enabled = sheet.reasonEnabled,
                )
                Text("${sheet.reasonCount} of ${sheet.minReason} characters", style = Ab.type.small, color = Ab.colors.muted)
            }
            sheet.lmpLine?.let { line ->
                Text(line, style = Ab.type.bodyMedium, color = Ab.colors.ink)
                if (LockMyPhoneInstaller.downloaded(context) == null || installMessage != null) {
                    Text("An update of LockMyPhone-Extended lets this phone release its lock when the session ends.", style = Ab.type.small, color = Ab.colors.muted)
                }
                AbTonalButton(
                    "Update LockMyPhone-Extended", enabled = !busy,
                    onClick = {
                        busy = true
                        installMessage = "Downloading LockMyPhone-Extended…"
                        scope.launch {
                            installMessage = when (val outcome = LockMyPhoneInstaller.download(context)) {
                                LockMyPhoneInstaller.Outcome.Ready ->
                                    if (LockMyPhoneInstaller.install(context)) null else "Allow this app to install apps on the page that opened, then come back and tap Update."
                                is LockMyPhoneInstaller.Outcome.Failed -> outcome.message
                            }
                            busy = false
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                )
                installMessage?.let { Text(it, style = Ab.type.small, color = Ab.colors.muted) }
            }
        }
    }
}

// The wait as a ring with its time in the middle: it empties as the wait runs, and says "0:00" when it is over.
@Composable
private fun WaitRing(waitSeconds: Int, leftSeconds: Int) {
    val progress = if (waitSeconds <= 0) 0f else (leftSeconds.toFloat() / waitSeconds).coerceIn(0f, 1f)
    val text = PhoneEndSheet.clock(leftSeconds)
    Row(
        Modifier.fillMaxWidth().semantics(mergeDescendants = true) { contentDescription = if (leftSeconds > 0) "$text of the wait left" else "The wait is over" },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Box(contentAlignment = Alignment.Center) {
            AbRing(progress, diameter = 64.dp, strokeWidth = 5.dp)
            Text(text, style = Ab.type.small.copy(fontFeatureSettings = "tnum"), color = Ab.colors.ink)
        }
        Text(if (leftSeconds > 0) "Wait for it, or confirm now: it ends by itself when the wait is over." else "The wait is over. Say why, then end.", style = Ab.type.bodyMedium, color = Ab.colors.muted, modifier = Modifier.weight(1f))
    }
}
