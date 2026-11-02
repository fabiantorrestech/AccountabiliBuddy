package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.height
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ChevronRight
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A row in a list card: icon, title, a one-line subtitle, and a chevron (or a lock while the page is resting). Rows of one
 * card are separated by a hairline ([divider]); the last row of a card turns it off. At least 56 dp tall.
 */
@Composable
fun AbListRow(
    title: String,
    modifier: Modifier = Modifier,
    icon: ImageVector? = null,
    subtitle: String? = null,
    onClick: (() -> Unit)? = null,
    locked: Boolean = false,
    showChevron: Boolean = true,
    divider: Boolean = false,
    trailing: (@Composable () -> Unit)? = null,
) {
    val colors = Ab.colors
    Column(modifier.fillMaxWidth()) {
        Row(
            Modifier.fillMaxWidth().heightIn(min = 56.dp).let { if (onClick != null) it.clickable(role = Role.Button, onClick = onClick) else it }.padding(horizontal = 4.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            if (icon != null) Icon(icon, contentDescription = null, tint = colors.muted, modifier = Modifier.size(24.dp))
            Column(Modifier.weight(1f)) {
                Text(title, style = Ab.type.body, fontWeight = FontWeight.Medium, color = colors.ink)
                if (subtitle != null) Text(subtitle, style = Ab.type.small, color = colors.muted)
            }
            trailing?.invoke()
            when {
                locked -> Icon(Icons.Outlined.Lock, contentDescription = "Resting while you focus", tint = colors.faint, modifier = Modifier.size(20.dp))
                showChevron && onClick != null -> Icon(Icons.Outlined.ChevronRight, contentDescription = null, tint = colors.faint, modifier = Modifier.size(24.dp))
            }
        }
        if (divider) Box(Modifier.fillMaxWidth().padding(start = if (icon != null) 44.dp else 4.dp).height(1.dp).background(colors.line))
    }
}
