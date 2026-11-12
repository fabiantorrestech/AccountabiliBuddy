package com.accountabilibuddy.companion.ui.sheets

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.ChevronRight
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * One row of an [AbSheet] (brief §3.7): at least 58 dp, a muted 22 dp [icon], the [title] at 15.5 sp, an optional muted [subtitle],
 * and a [trailing] side (a value with ›, a switch, a ✓ or a segmented control). A [danger] row is red (the drift colour) and goes
 * last. A row that is not [enabled] is drawn faded and can't be tapped; its subtitle says why ("Coming soon"). [onClick] null makes
 * a row that only shows something (its trailing control takes the touches).
 */
@Composable
fun AbSheetRow(
    title: String,
    onClick: (() -> Unit)?,
    modifier: Modifier = Modifier,
    icon: ImageVector? = null,
    subtitle: String? = null,
    enabled: Boolean = true,
    danger: Boolean = false,
    role: Role = Role.Button,
    trailing: (@Composable () -> Unit)? = null,
) {
    val colors = Ab.colors
    val ink = if (danger) colors.drift else colors.ink
    val clickable = if (onClick != null) Modifier.clickable(enabled = enabled, role = role, onClick = onClick) else Modifier
    Row(
        modifier.fillMaxWidth().heightIn(min = 58.dp).then(clickable).alpha(if (enabled) 1f else 0.45f)
            .padding(horizontal = 24.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        if (icon != null) Icon(icon, contentDescription = null, tint = if (danger) colors.drift else colors.muted, modifier = Modifier.size(22.dp))
        Column(Modifier.weight(1f)) {
            Text(title, style = Ab.type.bodyMedium.copy(fontSize = 15.5.sp), color = ink, maxLines = 2, overflow = TextOverflow.Ellipsis)
            if (subtitle != null) Text(subtitle, style = Ab.type.small, color = colors.muted, maxLines = 2, overflow = TextOverflow.Ellipsis)
        }
        trailing?.invoke()
    }
}

/** A row's right side: a muted value ("3 files") and, for a row that opens something, ›. */
@Composable
fun AbSheetValue(text: String?, opens: Boolean = true) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(2.dp)) {
        if (text != null) Text(text, style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(end = 2.dp))
        if (opens) Icon(Icons.Outlined.ChevronRight, contentDescription = null, tint = Ab.colors.muted, modifier = Modifier.size(20.dp))
    }
}

/** A row's right side for the chosen one of a list: an accent ✓ (nothing for the others, so the titles stay put). */
@Composable
fun AbSheetCheck(checked: Boolean) {
    if (checked) Icon(Icons.Outlined.Check, contentDescription = null, tint = Ab.colors.accent, modifier = Modifier.size(22.dp))
}
