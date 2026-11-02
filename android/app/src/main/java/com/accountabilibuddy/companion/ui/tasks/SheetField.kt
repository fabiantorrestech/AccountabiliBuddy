package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * A text box in the theme's tones, for the title, a step and the note in the detail sheet. [onDone] runs when the keyboard's
 * Done is pressed and when the box loses focus, so the sheet writes what was typed then (never on every key).
 */
@Composable
fun SheetField(
    value: String,
    onValueChange: (String) -> Unit,
    placeholder: String,
    onDone: () -> Unit,
    modifier: Modifier = Modifier,
    singleLine: Boolean = false,
    minLines: Int = 1,
    maxLines: Int = if (singleLine) 1 else 6,
) {
    val colors = Ab.colors
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        modifier = modifier.fillMaxWidth().onFocusChanged { if (!it.isFocused) onDone() },
        placeholder = { Text(placeholder, color = colors.faint) },
        textStyle = Ab.type.body,
        shape = Ab.shapes.mediumShape,
        singleLine = singleLine,
        minLines = minLines,
        maxLines = maxLines,
        keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences, imeAction = if (singleLine) ImeAction.Done else ImeAction.Default),
        keyboardActions = KeyboardActions(onDone = { onDone() }),
        colors = OutlinedTextFieldDefaults.colors(
            focusedTextColor = colors.ink, unfocusedTextColor = colors.ink,
            focusedBorderColor = colors.ink, unfocusedBorderColor = colors.line,
            cursorColor = colors.ink, focusedContainerColor = colors.surface, unfocusedContainerColor = colors.surface,
        ),
    )
}
