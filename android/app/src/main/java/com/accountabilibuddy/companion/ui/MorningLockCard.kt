package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.LockMyPhoneClient
import com.accountabilibuddy.companion.essentials.MorningLockState
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * "In the morning" (ADR 73, LMP ADR 0013): LockMyPhone-Extended's Morning lock, set up from here and kept there. It
 * starts at the first pickup after sleeping, or a set time before the first alarm; a day with no alarm can fall back
 * to the first pickup. It uses the night's LockMyPhone-Extended profile. What's shown is read back from
 * LockMyPhone-Extended, where it stays editable (the last change in either app wins). Needs LockMyPhone-Extended.
 */
@Composable
internal fun MorningLockCard() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val scope = rememberCoroutineScope()
    val usable = remember { app.lockScreen.usesLockMyPhone() && LockMyPhoneClient.supports(context, LockMyPhoneClient.SET_MORNING_LOCK) }
    var state by remember { mutableStateOf<MorningLockState?>(null) }
    var why by remember { mutableStateOf(false) }
    LaunchedEffect(usable) {
        if (usable) state = withContext(Dispatchers.IO) { LockMyPhoneClient.morningLock(context) } ?: DEFAULT.copy(profile = app.bedtimeEssentials.hardcoreProfile)
    }

    fun change(next: MorningLockState) {
        state = next
        scope.launch(Dispatchers.IO) { LockMyPhoneClient.setMorningLock(context, next) }
    }

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        AbSectionLabel("In the morning (LockMyPhone-Extended)")
        if (!usable) {
            Note("Needs LockMyPhone-Extended (0.14.0 or later) as the way your phone locks.", color = Ab.colors.muted)
            TextButton(onClick = { why = true }) { Text("Why lock the first hour?") }
            if (why) MorningWhyDialog { why = false }
            return@Column
        }
        val current = state ?: return@Column
        ToggleRow("Lock the start of the day", "LockMyPhone-Extended locks the phone for ${current.lockMinutes} minutes as your day starts", current.enabled) {
            change(current.copy(enabled = it))
        }
        if (current.enabled) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                GuardChip("At the first pickup", selected = !current.beforeAlarm, onClick = { change(current.copy(beforeAlarm = false)) })
                GuardChip("Before the first alarm", selected = current.beforeAlarm, onClick = { change(current.copy(beforeAlarm = true)) })
            }
            if (current.beforeAlarm) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    listOf(30, 60, 90).forEach { minutes ->
                        GuardChip("$minutes min before", selected = current.beforeAlarmMinutes == minutes, onClick = { change(current.copy(beforeAlarmMinutes = minutes)) })
                    }
                }
                ToggleRow("No alarm that day: lock at the first pickup", "Off: days without an alarm (weekends) aren't locked", current.noAlarmFallback) {
                    change(current.copy(noAlarmFallback = it))
                }
                Note("Locked from ${current.beforeAlarmMinutes} minutes before the first alarm until ${current.lockMinutes} minutes after you dismiss it. The alarm always rings. Only alarms your clock app gives Android are seen (Google Clock does).")
            } else {
                Note("Locks at your first pickup after sleeping (LockMyPhone-Extended tells sleep from charging, places or a dismissed alarm).")
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf(30, 60, 90).forEach { minutes ->
                    GuardChip("$minutes min lock", selected = current.lockMinutes == minutes, onClick = { change(current.copy(lockMinutes = minutes)) })
                }
            }
            Note("Kept in LockMyPhone-Extended as \"Accountabilibuddy morning\"; you can change it there too.")
        }
        TextButton(onClick = { why = true }) { Text("Why?") }
        if (why) MorningWhyDialog { why = false }
    }
}

/** Why a phone-free first hour: what the research does and doesn't show, each source with its kind of evidence. */
@Composable
internal fun MorningWhyDialog(onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Why keep the first hour phone-free?") },
        text = {
            Column(Modifier.abVerticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("Most of us reach for the phone first: in a 2013 survey of 7,446 US smartphone owners, 79% checked it within 15 minutes of waking (IDC, sponsored by Facebook; a survey).")
                Text("In lab studies, phones cost attention. Just seeing a notification made people worse at an ongoing task (Stothart et al., 2015). Having your own phone on the desk lowered working-memory scores compared with leaving it in another room (Ward et al., 2017), though a later study found no such cost (Hartmann, 2020). These are lab experiments.")
                Text("What isn't proven yet: no study has measured whether using the phone in your first hour makes you more distracted for the rest of the day. The idea rests on the findings above. A phone-free first hour is a cheap way to see the difference for yourself.")
                Note("Sources: IDC, \"Always Connected\" (2013). Stothart, Mitchum and Yehnert, J. Exp. Psychol.: Human Perception and Performance 41(4), 2015. Ward, Duke, Gneezy and Bos, J. Assoc. Consumer Research 2(2), 2017. Hartmann, 2020 (Swiss Distance University, accepted manuscript).")
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text("Done") } },
    )
}

private val DEFAULT = MorningLockState(
    enabled = false,
    beforeAlarm = false,
    beforeAlarmMinutes = 60,
    noAlarmFallback = true,
    lockMinutes = 60,
    profile = null,
)
