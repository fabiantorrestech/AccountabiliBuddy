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
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A quiet button with a hairline and no fill: the second button of a sheet's footer (Cancel, Back), "Target" beside Log weight, and,
 * with [danger], a destructive one in red text ("Delete area"; boards Goals-Phone-New, -New-2, -Area, -Weight).
 */
@Composable
fun AbOutlinedButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true, icon: ImageVector? = null, danger: Boolean = false) {
    val colors = Ab.colors
    Surface(
        onClick = onClick,
        modifier = modifier,
        enabled = enabled,
        shape = Ab.shapes.pill,
        color = colors.surface.copy(alpha = 0f),
        contentColor = if (!enabled) colors.faint else if (danger) colors.drift else colors.ink,
        border = BorderStroke(1.dp, colors.line),
    ) {
        Row(
            Modifier.heightIn(min = 52.dp).padding(horizontal = 16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.CenterHorizontally),
        ) {
            if (icon != null) Icon(icon, contentDescription = null, modifier = Modifier.size(20.dp))
            Text(text, style = Ab.type.body, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}
