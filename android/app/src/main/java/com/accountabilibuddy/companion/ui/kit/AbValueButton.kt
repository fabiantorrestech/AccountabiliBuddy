package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A small outlined pill that shows a value and opens a way to change it (a time, a category, a day type, "Add week"). Ink on
 * the surface with a hairline; the touch target is 48 dp even though the pill is shorter. Many may share a screen.
 */
@Composable
fun AbValueButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true, icon: ImageVector? = null) {
    val colors = Ab.colors
    Surface(
        onClick = onClick,
        enabled = enabled,
        modifier = modifier.minimumInteractiveComponentSize().semantics { role = Role.Button },
        shape = Ab.shapes.pill,
        color = colors.surface,
        contentColor = if (enabled) colors.ink else colors.faint,
        border = BorderStroke(1.dp, colors.line),
    ) {
        Row(
            Modifier.heightIn(min = 36.dp).padding(horizontal = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            if (icon != null) Icon(icon, contentDescription = null, modifier = Modifier.size(18.dp))
            Text(text, style = Ab.type.small, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}
