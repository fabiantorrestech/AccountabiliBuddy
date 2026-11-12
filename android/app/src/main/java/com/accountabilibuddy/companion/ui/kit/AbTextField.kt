package com.accountabilibuddy.companion.ui.kit

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.VisualTransformation
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A text box in the theme's tones (hairline edge, ink when focused, no accent), with its [label] inside. Writes on every key:
 * for the editors that keep a draft. (The task sheet has its own that writes when the box loses focus.)
 */
@Composable
fun AbTextField(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    modifier: Modifier = Modifier,
    singleLine: Boolean = true,
    enabled: Boolean = true,
    /** Text that is not a sentence (an address, a key) turns the capital off. */
    capitalization: KeyboardCapitalization = KeyboardCapitalization.Sentences,
    visualTransformation: VisualTransformation = VisualTransformation.None,
    isError: Boolean = false,
    /** A number box asks for the number keyboard. */
    keyboardType: androidx.compose.ui.text.input.KeyboardType = androidx.compose.ui.text.input.KeyboardType.Unspecified,
) {
    val colors = Ab.colors
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        modifier = modifier.fillMaxWidth(),
        enabled = enabled,
        label = { Text(label) },
        textStyle = Ab.type.body,
        shape = Ab.shapes.mediumShape,
        singleLine = singleLine,
        keyboardOptions = KeyboardOptions(capitalization = capitalization, keyboardType = keyboardType),
        visualTransformation = visualTransformation,
        isError = isError,
        colors = OutlinedTextFieldDefaults.colors(
            focusedTextColor = colors.ink, unfocusedTextColor = colors.ink, disabledTextColor = colors.faint,
            focusedBorderColor = colors.ink, unfocusedBorderColor = colors.line, disabledBorderColor = colors.line,
            focusedLabelColor = colors.muted, unfocusedLabelColor = colors.muted, disabledLabelColor = colors.faint,
            cursorColor = colors.ink, focusedContainerColor = colors.surface, unfocusedContainerColor = colors.surface,
            disabledContainerColor = colors.surface,
        ),
    )
}
