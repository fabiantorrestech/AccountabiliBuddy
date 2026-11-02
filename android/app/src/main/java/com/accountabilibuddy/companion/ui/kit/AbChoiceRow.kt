package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.toggleable
import androidx.compose.material3.RadioButton
import androidx.compose.material3.RadioButtonDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/** One option of a single choice: a radio mark in ink (never accent), the text, and an optional muted line. The whole row selects. */
@Composable
fun AbChoiceRow(text: String, selected: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier, subtitle: String? = null, enabled: Boolean = true) {
    val colors = Ab.colors
    Row(
        modifier.fillMaxWidth().heightIn(min = 48.dp).selectable(selected = selected, enabled = enabled, role = Role.RadioButton, onClick = onClick),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        RadioButton(
            selected = selected,
            onClick = null,
            enabled = enabled,
            colors = RadioButtonDefaults.colors(selectedColor = colors.ink, unselectedColor = colors.faint, disabledSelectedColor = colors.faint, disabledUnselectedColor = colors.line),
            modifier = Modifier.padding(start = 4.dp),
        )
        Column(Modifier.weight(1f)) {
            Text(text, style = Ab.type.bodyMedium, fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal, color = if (enabled) colors.ink else colors.faint)
            if (subtitle != null) Text(subtitle, style = Ab.type.small, color = colors.muted)
        }
    }
}

/** A tick option: the kit's [AbCheck] box with its text; the whole row toggles. Ink, never accent. */
@Composable
fun AbCheckRow(text: String, checked: Boolean, onCheckedChange: (Boolean) -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true) {
    val colors = Ab.colors
    Row(
        modifier.fillMaxWidth().heightIn(min = 48.dp).toggleable(value = checked, enabled = enabled, role = Role.Checkbox, onValueChange = onCheckedChange),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        AbCheck(checked, Modifier.padding(start = 4.dp))
        Text(text, style = Ab.type.bodyMedium, color = if (enabled) colors.ink else colors.faint, modifier = Modifier.weight(1f))
    }
}
