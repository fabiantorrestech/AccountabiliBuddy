package com.accountabilibuddy.companion.ui.kit

import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The kit's switch: an ink track when on, never the accent; a faint, flat one while it can't be changed (a guard resting during
 * a session). Drawn only: the row around it carries the click and the semantics ([AbToggleRow] does).
 */
@Composable
fun AbSwitch(checked: Boolean, modifier: Modifier = Modifier, enabled: Boolean = true, onCheckedChange: ((Boolean) -> Unit)? = null) {
    val colors = Ab.colors
    Switch(
        checked = checked,
        onCheckedChange = onCheckedChange,
        modifier = modifier,
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
