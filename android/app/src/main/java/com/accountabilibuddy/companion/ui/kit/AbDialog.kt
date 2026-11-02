package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A dialog in the theme's surface (Material 3's AlertDialog). Buttons are text buttons in ink; the confirm one is bold.
 * Leave [confirmText] null for a dialog with only a dismiss button.
 */
@Composable
fun AbDialog(
    title: String,
    onDismiss: () -> Unit,
    confirmText: String? = null,
    onConfirm: () -> Unit = {},
    dismissText: String = "Cancel",
    content: @Composable ColumnScope.() -> Unit,
) {
    val colors = Ab.colors
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = colors.surface,
        titleContentColor = colors.ink,
        textContentColor = colors.ink,
        shape = Ab.shapes.cardShape,
        title = { Text(title, style = Ab.type.section) },
        text = { Column(verticalArrangement = Arrangement.spacedBy(10.dp), content = content) },
        confirmButton = {
            if (confirmText != null) TextButton(onClick = onConfirm) { Text(confirmText, color = colors.ink, fontWeight = FontWeight.Bold) }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(dismissText, color = colors.muted) } },
    )
}
