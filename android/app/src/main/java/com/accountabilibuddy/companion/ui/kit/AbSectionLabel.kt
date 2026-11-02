package com.accountabilibuddy.companion.ui.kit

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import com.accountabilibuddy.companion.ui.theme.Ab

/** The small muted label at the top of a card ("Theme", "Light or dark", "Come back to"). */
@Composable
fun AbSectionLabel(text: String, modifier: Modifier = Modifier) {
    Text(text, modifier = modifier, style = Ab.type.bodyMedium, fontWeight = FontWeight.Medium, color = Ab.colors.muted)
}
