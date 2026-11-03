package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ProfileColors
import com.accountabilibuddy.companion.model.FocusProfile
import com.accountabilibuddy.companion.model.MatchTarget
import com.accountabilibuddy.companion.model.RuleVerdict
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbListRow
import com.accountabilibuddy.companion.ui.theme.Ab

// The pieces of Settings › Guards' profiles, read only: a device's row (this phone, then the PC) and one PC profile as a card (colour
// and rules; tap to see the rules). The page that lists them is settings/GuardsPage.

/** A device's row: its name, a line about its profiles and ▾/▴; tap to open or close what is under it. */
@Composable
internal fun ProfileDeviceRow(title: String, icon: ImageVector, detail: String, expanded: Boolean, onToggle: () -> Unit) {
    AbCard(onClick = onToggle, contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
        AbListRow(
            title,
            icon = icon,
            subtitle = detail,
            showChevron = false,
            trailing = {
                Icon(
                    if (expanded) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore,
                    contentDescription = if (expanded) "Fold" else "Open", tint = Ab.colors.faint, modifier = Modifier.size(24.dp),
                )
            },
        )
    }
}

/** One PC profile: its colour dot, its name and how many sites and apps it blocks and allows; a tap lists the rules. */
@Composable
internal fun ProfileCard(profile: FocusProfile) {
    var expanded by remember { mutableStateOf(false) }
    val blocked = profile.rules.count { it.verdict == RuleVerdict.Block }
    val allowed = profile.rules.size - blocked

    AbCard(onClick = { expanded = !expanded }, verticalSpacing = 6.dp) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            // The profile's own colour, as the PC shows it.
            Box(Modifier.size(12.dp).background(Color(ProfileColors.argbFor(profile.id)), CircleShape))
            Spacer(Modifier.width(10.dp))
            Text(profile.name, style = Ab.type.body, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
        }
        Note("$blocked blocked · $allowed allowed")
        if (expanded) {
            profile.rules.forEach { rule ->
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text(
                        if (rule.verdict == RuleVerdict.Block) "Block" else "Allow",
                        modifier = Modifier.width(52.dp),
                        color = if (rule.verdict == RuleVerdict.Block) Ab.colors.drift else Ab.colors.met,
                        style = Ab.type.small,
                        fontWeight = FontWeight.Medium,
                    )
                    Text("${targetName(rule.target)}: ${rule.pattern}", style = Ab.type.small, color = Ab.colors.ink)
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
