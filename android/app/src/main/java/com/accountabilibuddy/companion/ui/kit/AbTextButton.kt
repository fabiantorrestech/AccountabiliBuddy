package com.accountabilibuddy.companion.ui.kit

import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import com.accountabilibuddy.companion.ui.theme.Ab

/** A quiet text button in ink ("Edit", "Done", "Discard"): never the accent. [danger] tints it with the drift colour for a deletion. */
@Composable
fun AbTextButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true, danger: Boolean = false) {
    val colors = Ab.colors
    val tint: Color = if (danger) colors.drift else colors.ink
    TextButton(
        onClick = onClick,
        modifier = modifier,
        enabled = enabled,
        colors = ButtonDefaults.textButtonColors(contentColor = tint, disabledContentColor = colors.faint),
    ) {
        Text(text, style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold)
    }
}
