package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A tick box for a task, a step or a come-back: an empty rounded square with a hairline, or the second tone with a check once done.
 * Ink, never accent. Drawn only; the row around it carries the click and the semantics.
 */
@Composable
fun AbCheck(checked: Boolean, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Surface(
        modifier = modifier.size(24.dp),
        shape = RoundedCornerShape(7.dp),
        color = if (checked) colors.surface2 else colors.bg.copy(alpha = 0f),
        border = if (checked) null else BorderStroke(1.5.dp, colors.faint),
    ) {
        Box(contentAlignment = Alignment.Center) {
            if (checked) Icon(Icons.Outlined.Check, contentDescription = null, tint = colors.muted, modifier = Modifier.size(16.dp))
        }
    }
}
