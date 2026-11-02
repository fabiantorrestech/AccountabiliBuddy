package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.selection.toggleable
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/** A switch with its title and a muted line under it. On is an ink track, never the accent. The whole row toggles. */
@Composable
fun AbToggleRow(
    title: String,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    modifier: Modifier = Modifier,
    subtitle: String? = null,
    enabled: Boolean = true,
) {
    val colors = Ab.colors
    Row(
        modifier.fillMaxWidth().heightIn(min = 56.dp).toggleable(value = checked, enabled = enabled, role = Role.Switch, onValueChange = onCheckedChange).padding(horizontal = 4.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Column(Modifier.weight(1f)) {
            Text(title, style = Ab.type.body, fontWeight = FontWeight.Medium, color = if (enabled) colors.ink else colors.faint)
            if (subtitle != null) Text(subtitle, style = Ab.type.small, color = colors.muted)
        }
        Switch(
            checked = checked,
            onCheckedChange = null,
            enabled = enabled,
            colors = SwitchDefaults.colors(
                checkedTrackColor = colors.ink,
                checkedThumbColor = colors.bg,
                checkedBorderColor = colors.ink,
                uncheckedTrackColor = colors.surface2,
                uncheckedThumbColor = colors.muted,
                uncheckedBorderColor = colors.line,
                disabledCheckedTrackColor = colors.faint,
                disabledCheckedThumbColor = colors.bg,
                disabledCheckedBorderColor = colors.faint,
                disabledUncheckedTrackColor = colors.surface2,
                disabledUncheckedThumbColor = colors.faint,
                disabledUncheckedBorderColor = colors.line,
            ),
        )
    }
}
