package com.accountabilibuddy.companion.ui

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
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.ProfileColors
import com.accountabilibuddy.companion.model.FocusProfile
import com.accountabilibuddy.companion.model.MatchTarget
import com.accountabilibuddy.companion.model.RuleVerdict
import java.time.format.TextStyle
import java.util.Locale

/** The imported profiles (read only for now): colour, rules and when each one is scheduled. Tap to see the rules. */
@Composable
fun ProfilesScreen(state: CompanionState, clock: ClockFormat) {
    if (state.document.profiles.isEmpty()) {
        Text("No profiles yet. Import a backup from the PC in Settings.", modifier = Modifier.padding(16.dp))
        return
    }

    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        item {
            Text(
                "${state.importedFrom ?: "From the PC"}; imported ${state.importedAt ?: ""}. Edit profiles on the PC and import again.",
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        items(state.document.profiles, key = { it.id }) { profile -> ProfileCard(profile, state, clock) }
    }
}

@Composable
private fun ProfileCard(profile: FocusProfile, state: CompanionState, clock: ClockFormat) {
    var expanded by remember { mutableStateOf(false) }
    val blocks = state.document.schedule.blocks.filter { it.profileId == profile.id }
        .sortedWith(compareBy({ (it.day.value % 7) }, { it.start }))
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
            if (blocks.isNotEmpty()) {
                val byDay = blocks.groupBy { it.day }
                Text(
                    byDay.entries.joinToString("\n") { (day, dayBlocks) ->
                        "${day.getDisplayName(TextStyle.SHORT, Locale.getDefault())}: " + dayBlocks.joinToString(", ") {
                            "${clock.time(java.time.LocalDate.now().atTime(it.start))}–${clock.time(java.time.LocalDate.now().atTime(it.end))}"
                        }
                    },
                    style = MaterialTheme.typography.bodySmall,
                )
            }
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
