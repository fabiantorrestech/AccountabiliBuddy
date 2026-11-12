package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The small segmented control that sits at the right of a row (brief §3.7; boards Goals-Phone-Options, Today-Layout-Phone): a 34 dp
 * pill with a hairline, 30 dp options at 13 sp, each as wide as its label, so the row keeps its height and its title doesn't wrap.
 * The full-width [AbSegmented] stays for a choice that has a line of its own.
 */
@Composable
fun AbSegmentedCompact(options: List<String>, selectedIndex: Int?, onSelect: (Int) -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true) {
    val colors = Ab.colors
    Row(
        modifier.height(34.dp).border(1.dp, colors.line, Ab.shapes.pill).padding(horizontal = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        options.forEachIndexed { index, label ->
            val selected = index == selectedIndex
            Surface(
                onClick = { onSelect(index) },
                enabled = enabled,
                modifier = Modifier.height(30.dp).semantics { this.selected = selected; role = Role.RadioButton },
                shape = Ab.shapes.pill,
                color = if (selected) colors.surface2 else colors.surface2.copy(alpha = 0f),
                contentColor = if (selected) colors.ink else colors.muted,
            ) {
                Box(Modifier.padding(horizontal = 10.dp), contentAlignment = Alignment.Center) {
                    Text(label, style = Ab.type.small.copy(fontSize = 13.sp), fontWeight = if (selected) FontWeight.Bold else FontWeight.Medium, maxLines = 1)
                }
            }
        }
    }
}
