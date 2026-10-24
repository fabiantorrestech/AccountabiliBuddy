package com.accountabilibuddy.companion.ui

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Computer
import androidx.compose.material.icons.outlined.Smartphone
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.link.PcState
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.ProfileColors
import com.accountabilibuddy.companion.model.FocusProfile
import com.accountabilibuddy.companion.model.MatchTarget
import com.accountabilibuddy.companion.model.RuleVerdict

/**
 * Profiles by device, read only: this phone first with what actually guards it ([PhoneGuards]: Curbox, LockMyPhone,
 * essentials, emergency time, the work card), then the PC's profiles (colour and rules; tap to see the rules; profile
 * count, what it's running, when they synced).
 */
@Composable
fun ProfilesScreen(
    state: CompanionState,
    clock: ClockFormat,
    pcName: String? = null,
    pcState: PcState? = null,
    deviceName: String = "This phone",
    curbox: com.accountabilibuddy.companion.curbox.CurboxStatus = com.accountabilibuddy.companion.curbox.CurboxStatus(),
) {
    var pcExpanded by remember { mutableStateOf(true) }
    var phoneExpanded by remember { mutableStateOf(true) }
    val context = androidx.compose.ui.platform.LocalContext.current
    val guards = remember(curbox) { PhoneGuards.read(context, curbox) }
    val profiles = state.document.profiles
    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        // This phone first: what actually guards it.
        item {
            DeviceRow(
                title = "$deviceName (this phone)",
                icon = Icons.Outlined.Smartphone,
                detail = PhoneGuards.summary(guards),
                expanded = phoneExpanded,
                onToggle = { phoneExpanded = !phoneExpanded },
            )
        }
        if (phoneExpanded) {
            item { PhoneGuardCards(guards, pcName) }
        }

        if (profiles.isEmpty()) {
            item { Text("No PC profiles yet. Link the PC or import a backup from it in Settings.", modifier = Modifier.padding(top = 8.dp)) }
            return@LazyColumn
        }

        if (pcName == null) {
            item {
                Text(
                    "${state.importedFrom ?: "From the PC"}; imported ${state.importedAt ?: ""}. Edit profiles on the PC and import again.",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            items(profiles, key = { it.id }) { profile -> ProfileCard(profile, state, clock) }
            return@LazyColumn
        }

        val running = when {
            pcState == null -> "not connected right now"
            pcState.isScheduledBlock -> "blocks on: ${pcState.profileName ?: "a profile"}"
            pcState.isActive -> "running: ${pcState.profileName ?: "a profile"}"
            else -> "nothing running"
        }
        item {
            DeviceRow(
                title = pcName,
                icon = Icons.Outlined.Computer,
                detail = "${profiles.size} profile${if (profiles.size == 1) "" else "s"} · $running · synced ${state.importedAt ?: "not yet"}",
                expanded = pcExpanded,
                onToggle = { pcExpanded = !pcExpanded },
            )
        }
        if (pcExpanded) {
            items(profiles, key = { it.id }) { profile ->
                Column(Modifier.padding(start = 16.dp)) { ProfileCard(profile, state, clock) }
            }
        }

    }
}

/** A device's row: ▾/▸, its name, and a line about its profiles; tap to open or close. */
@Composable
private fun DeviceRow(title: String, icon: androidx.compose.ui.graphics.vector.ImageVector, detail: String, expanded: Boolean, onToggle: () -> Unit) {
    Column(Modifier.fillMaxWidth().clickable(onClick = onToggle).padding(vertical = 4.dp)) {
        IconTitle("${if (expanded) "▾" else "▸"} $title", style = MaterialTheme.typography.titleMedium, icon = icon, fontWeight = FontWeight.SemiBold)
        Text(detail, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(start = 46.dp))
    }
}

@Composable
private fun ProfileCard(profile: FocusProfile, state: CompanionState, clock: ClockFormat) {
    var expanded by remember { mutableStateOf(false) }
    val blocked = profile.rules.count { it.verdict == RuleVerdict.Block }
    val allowed = profile.rules.size - blocked

    Card(Modifier.fillMaxWidth().clickable { expanded = !expanded }) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(12.dp).background(Color(ProfileColors.argbFor(profile.id)), CircleShape))
                Spacer(Modifier.width(10.dp))
                Text(profile.name, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
            }
            Text("$blocked blocked · $allowed allowed", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            if (expanded) {
                profile.rules.forEach { rule ->
                    Row {
                        Text(
                            if (rule.verdict == RuleVerdict.Block) "Block" else "Allow",
                            modifier = Modifier.width(52.dp),
                            color = if (rule.verdict == RuleVerdict.Block) Color(0xFFFCA5A5) else Color(0xFF86EFAC),
                            style = MaterialTheme.typography.labelMedium,
                        )
                        Text(
                            "${targetName(rule.target)}: ${rule.pattern}",
                            style = MaterialTheme.typography.bodySmall,
                        )
                    }
                }
            }
        }
    }
}

private fun targetName(target: MatchTarget): String = when (target) {
    MatchTarget.ProcessName -> "App"
    MatchTarget.WindowTitle -> "Title"
    MatchTarget.Url -> "Site"
    MatchTarget.OcrText -> "Screen text"
    MatchTarget.Channel -> "Channel"
}
