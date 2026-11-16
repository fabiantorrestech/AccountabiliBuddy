package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.PhoneAndroid
import androidx.compose.material.icons.outlined.PlayArrow
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.companion.session.PhoneSessionStarter
import com.accountabilibuddy.companion.sync.PcFileControl
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.ui.TodayPc
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.session.PhoneSessionEngine
import com.accountabilibuddy.shared.session.PhoneSessionMode
import com.accountabilibuddy.shared.session.PhoneSessionProfile
import com.accountabilibuddy.shared.session.PhoneStartRules
import com.accountabilibuddy.shared.session.StartPc
import com.accountabilibuddy.shared.session.StartTarget
import com.accountabilibuddy.shared.sync.GroupSessions
import com.accountabilibuddy.shared.sync.ReceivedSyncFile
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

private val PC_LENGTHS = listOf(25, 50, 90)
private val PHONE_LENGTHS = listOf(25, 50, 90)

/** The length a phone session starts with, as the chips choose it. */
private sealed interface PhoneLength {
    data class Minutes(val value: Int) : PhoneLength
    data object Custom : PhoneLength
    data object NoSetEnd : PhoneLength
}

/**
 * Start (PS3a, plan R16; no board: built from [AbSheet] and the R2b sheets' parts): where the session runs, "On <PC>" or "On this phone", then what each takes.
 * The remembered choice wins when it is available; the first time (or when the remembered PC is not available) it is the controlled PC when it is online, else
 * this phone, with "<PC> isn't online (last heard 2:05 PM)" under the PC option ([PhoneStartRules.defaultTarget], pure and checked). With no PC in the group only
 * "On this phone" is offered and nothing is sent (R15). "On <PC>" is the start Today always had (ADR 42: profile, length, or the PC's quick starts, through the sync
 * files); "On this phone" starts the session here from the chosen session profile ([PhoneSessionStarter]).
 *
 * Today only opens it while R1 allows a start; the controller refuses a start over a live session anyway.
 */
@Composable
fun StartSessionSheetHost(
    pc: TodayPc,
    received: List<ReceivedDevice>,
    clock: ClockFormat,
    staleLine: String?,
    onStartOnPc: (profileId: String?, minutes: Int?, countUp: Boolean) -> Unit,
    onMessage: (String) -> Unit,
    onDismiss: () -> Unit,
) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val starter = app.phoneSession.starter
    val zone = ZoneId.systemDefault()
    val time = { at: Instant -> clock.time(LocalDateTime.ofInstant(at, zone)) }
    val device = pc.pcId?.let { id -> received.firstOrNull { it.file.device.deviceId == id } }
    val startPc = remember(device, device?.receivedAt) {
        device?.let { d ->
            val now = TrustedTime.now()
            val heard = GroupSessions.heardAt(d.file.device, d.receivedAt)
            val live = PcFileControl.groupLive(ReceivedSyncFile(d.file, d.receivedAt), now) != null
            StartPc(d.file.device.deviceId.toString(), d.file.device.name, PhoneStartRules.pcOnline(heard, now, PcFileControl.supportsControl(d.file.device), live), heard)
        }
    }
    var target by remember { mutableStateOf(PhoneStartRules.defaultTarget(starter.rememberedTarget(), startPc)) }
    val mode = EssentialsLaunch.mode(app)

    AbSheet(
        title = "Start a session",
        subtitle = staleLine,
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        aboveKeyboard = true,
    ) {
        Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            if (PhoneStartRules.offersPc(startPc) && startPc != null) {
                AbSegmented(
                    modifier = Modifier.padding(horizontal = 24.dp),
                    options = listOf("On ${startPc.name}", "On this phone"),
                    selectedIndex = if (target is StartTarget.Pc) 0 else 1,
                    onSelect = { index ->
                        target = if (index == 0) StartTarget.Pc(startPc.deviceId) else StartTarget.Phone
                        starter.remember(target)
                    },
                    groupName = "Where the session runs",
                )
                // Under the PC option: why it can't take a start now (R16), or that its app needs an update.
                (pc.updateNeeded ?: PhoneStartRules.pcNote(startPc, time))?.let { Text(it, style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 24.dp)) }
            } else {
                // R15: no group or no PC. Only the phone; nothing is sent and nothing reads as an error.
                Row(Modifier.padding(horizontal = 24.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    Icon(Icons.Outlined.PhoneAndroid, contentDescription = null, tint = Ab.colors.muted, modifier = Modifier.size(22.dp))
                    Text("On this phone", style = Ab.type.bodyMedium, color = Ab.colors.ink)
                }
            }
            when (val shown = target) {
                is StartTarget.Pc -> if (startPc != null) {
                    PcPanel(pc, startPc, onStart = { profileId, minutes, countUp ->
                        starter.remember(shown)
                        onStartOnPc(profileId, minutes, countUp)
                        onDismiss()
                    })
                }
                StartTarget.Phone -> PhonePanel(app, mode, onStarted = onDismiss, onMessage = onMessage)
            }
        }
    }
}

// "On <PC>": the start Today always had (ADR 42). Offline, or too old to answer, nothing here starts.
@Composable
private fun PcPanel(pc: TodayPc, startPc: StartPc, onStart: (String?, Int?, Boolean) -> Unit) {
    val profiles = pc.state?.profiles.orEmpty()
    var profileId by remember { mutableStateOf(profiles.firstOrNull()?.id) }
    var minutes by remember { mutableIntStateOf(PC_LENGTHS.first()) }
    val canStart = startPc.online
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Column(Modifier.padding(horizontal = 24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            if (profiles.isNotEmpty()) {
                AbSectionLabel("Profile")
                AbChipRow { profiles.forEach { profile -> AbChip(profile.name, selected = profileId == profile.id, onClick = { profileId = profile.id }, enabled = canStart) } }
            }
            AbSectionLabel("Length")
            AbChipRow { PC_LENGTHS.forEach { length -> AbChip("$length min", selected = minutes == length, onClick = { minutes = length }, enabled = canStart) } }
            AbPrimaryButton("Start on ${startPc.name}", onClick = { onStart(profileId, minutes, false) }, enabled = canStart && profileId != null, icon = Icons.Outlined.PlayArrow, modifier = Modifier.fillMaxWidth())
        }
        // No length: the PC runs its default profile for the length it remembers, counting down. Or explicitly with no set end.
        AbSheetRow("Quick Start with the PC's default profile", onClick = { onStart(null, null, false) }, enabled = canStart)
        AbSheetRow("No set end with the PC's default profile", onClick = { onStart(null, null, true) }, enabled = canStart)
    }
}

// "On this phone": the session profile and the length (25, 50, 90, Custom, or no set end).
@Composable
private fun PhonePanel(app: CompanionApp, mode: PhoneSessionMode, onStarted: () -> Unit, onMessage: (String) -> Unit) {
    val starter = app.phoneSession.starter
    val profiles = remember { app.phoneSessionProfiles.choices() }
    var profileId by remember { mutableStateOf(starter.lastProfileId()?.takeIf { id -> profiles.any { it.id == id } } ?: profiles.first().id) }
    val profile = profiles.first { it.id == profileId }
    var length by remember(profileId) { mutableStateOf(initialLength(profile)) }
    var custom by remember(profileId) { mutableStateOf(if (initialLength(profile) == PhoneLength.Custom) profile.defaultMinutes.toString() else "") }
    var error by remember { mutableStateOf<String?>(null) }

    fun minutes(): Int? = when (val choice = length) {
        is PhoneLength.Minutes -> choice.value
        PhoneLength.Custom -> custom.trim().toIntOrNull()?.takeIf { it in 1..PhoneSessionEngine.MAX_MINUTES }
        PhoneLength.NoSetEnd -> null
    }
    val lengthOk = length != PhoneLength.Custom || minutes() != null

    Column(Modifier.padding(horizontal = 24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        AbSectionLabel("Profile")
        AbChipRow { profiles.forEach { p -> AbChip(p.name, selected = p.id == profileId, onClick = { profileId = p.id; error = null }) } }
        Text(profileLine(profile), style = Ab.type.small, color = Ab.colors.muted)
        AbSectionLabel("Length")
        AbChipRow {
            PHONE_LENGTHS.forEach { value -> AbChip("$value min", selected = length == PhoneLength.Minutes(value), onClick = { length = PhoneLength.Minutes(value); error = null }) }
            AbChip("Custom", selected = length == PhoneLength.Custom, onClick = { length = PhoneLength.Custom; error = null })
            AbChip("No set end", selected = length == PhoneLength.NoSetEnd, onClick = { length = PhoneLength.NoSetEnd; error = null })
        }
        if (length == PhoneLength.Custom) {
            AbTextField(
                value = custom, onValueChange = { custom = it.filter(Char::isDigit).take(3); error = null }, label = "Minutes (1 to ${PhoneSessionEngine.MAX_MINUTES})",
                keyboardType = KeyboardType.Number, isError = !lengthOk && custom.isNotEmpty(),
            )
        }
        Text(
            if (mode == PhoneSessionMode.ACCOUNTABILI_BUDDY) "Settings and your plan rest while it runs. Ending early takes a wait and a reason."
            else "It locks only what the profile turns on, and you can end it any time.",
            style = Ab.type.small, color = Ab.colors.muted,
        )
        error?.let { Text(it, style = Ab.type.bodyMedium, color = Ab.colors.drift) }
        AbPrimaryButton(
            "Start", enabled = lengthOk, icon = Icons.Outlined.PlayArrow, modifier = Modifier.fillMaxWidth(),
            onClick = {
                when (val result = starter.start(profile, minutes())) {
                    PhoneSessionStarter.Result.Started -> onStarted()
                    is PhoneSessionStarter.Result.Failed -> { error = result.text; onMessage(result.text) }
                }
            },
        )
    }
}

// The profile's default length as the chips show it: a no-set-end profile, one of the lengths, or Custom with its own number.
private fun initialLength(profile: PhoneSessionProfile): PhoneLength = when {
    profile.noSetEnd -> PhoneLength.NoSetEnd
    profile.defaultMinutes in PHONE_LENGTHS -> PhoneLength.Minutes(profile.defaultMinutes)
    profile.defaultMinutes in 1..PhoneSessionEngine.MAX_MINUTES -> PhoneLength.Custom
    else -> PhoneLength.Minutes(PHONE_LENGTHS[1])
}

// What choosing the profile turns on, in a line: a plain timer says so.
private fun profileLine(profile: PhoneSessionProfile): String {
    val parts = buildList {
        profile.lmpProfile?.let { add("LockMyPhone-Extended \"$it\"") }
        profile.curboxFocusGroup?.let { add("a Curbox focus group") }
        if (profile.curboxBlocks.isNotEmpty()) add("${profile.curboxBlocks.size} Curbox block${if (profile.curboxBlocks.size == 1) "" else "s"}")
    }
    return if (parts.isEmpty()) "A plain timer with This session's tasks." else "Turns on ${parts.joinToString(", ")}."
}
