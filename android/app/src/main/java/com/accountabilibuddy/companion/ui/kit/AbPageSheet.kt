package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A full-height sheet for a flow that takes pages (remove a device, restore a backup): an icon and [title] with a muted [subtitle]
 * (e.g. "Step 2 of 4") at the top, the page [content] scrolling in the middle, and the [footer] (the page's buttons) pinned at the
 * bottom. With [dismissible] false the sheet stays when swiped, tapped outside or backed out of, for the moment work is going on.
 */
@Composable
fun AbPageSheet(
    title: String,
    onDismiss: () -> Unit,
    modifier: Modifier = Modifier,
    icon: ImageVector? = null,
    subtitle: String = "",
    dismissible: Boolean = true,
    footer: @Composable ColumnScope.() -> Unit = {},
    content: @Composable ColumnScope.() -> Unit,
) {
    AbSheet(onDismiss, modifier.fillMaxHeight(), dismissible = dismissible) {
        Row(verticalAlignment = Alignment.Top, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            if (icon != null) Icon(icon, contentDescription = null, tint = Ab.colors.muted, modifier = Modifier.size(28.dp))
            Column(Modifier.weight(1f)) {
                Text(title, style = Ab.type.head, color = Ab.colors.ink, maxLines = 3)
                if (subtitle.isNotEmpty()) Text(subtitle, style = Ab.type.small, color = Ab.colors.muted)
            }
        }
        Column(Modifier.weight(1f).fillMaxWidth().verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp), content = content)
        Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp), content = footer)
    }
}
