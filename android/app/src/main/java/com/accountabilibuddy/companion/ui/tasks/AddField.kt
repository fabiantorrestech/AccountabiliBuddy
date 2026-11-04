package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.layout.Column
import androidx.compose.material3.TextButton
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.automirrored.outlined.KeyboardReturn
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * "Add a task" (board 5v): a pill with a plus, the text and, once something is typed, a return button; the keyboard's Done
 * does the same. [onSubmit] gets the trimmed text and the field clears at once (the text is the user's private text: it is kept
 * in memory only, never in the saved state). The plus is the accent when [accentPlus] (the one accent of the pane); a field that
 * can't be used now says why in [placeholder] and is [enabled] = false. A caller that must keep a half-typed text (the sheet's
 * step) passes its own [typed] state and reads it when it closes.
 */
@Composable
fun AddField(
    placeholder: String,
    onSubmit: (String) -> Unit,
    modifier: Modifier = Modifier,
    accentPlus: Boolean = false,
    enabled: Boolean = true,
    typed: MutableState<String> = remember { mutableStateOf("") },
    /** Tasks that match the text, shown above the field while typing; the keyboard's Done still adds what was typed. */
    suggest: ((String) -> List<TaskRowModel>)? = null,
    onPick: (TaskRowModel) -> Unit = {},
    /** Opens the "From your lists" sheet; no button when null. */
    onBrowse: (() -> Unit)? = null,
    /** Drawn after the plus: the category new tasks go in, on the Tasks tab. */
    leading: (@Composable () -> Unit)? = null,
) {
    val colors = Ab.colors
    var text by typed
    fun submit() {
        val typed = text.trim()
        if (typed.isNotEmpty()) {
            text = ""
            onSubmit(typed)
        }
    }

    val suggestions = if (enabled && text.trim().length >= MIN_TYPED_FOR_SUGGESTIONS) suggest?.invoke(text.trim()).orEmpty().take(MAX_SUGGESTIONS) else emptyList()
    Column(modifier.fillMaxWidth()) {
    suggestions.forEach { row ->
        TaskPickRow(row, onClick = {
            text = ""
            onPick(row)
        })
    }
    Surface(Modifier.fillMaxWidth().heightIn(min = 56.dp), shape = Ab.shapes.pill, color = colors.surface2, contentColor = colors.muted) {
        Row(Modifier.padding(start = 16.dp, end = 4.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Icon(Icons.Outlined.Add, contentDescription = null, tint = if (accentPlus && enabled) colors.accent else colors.muted, modifier = Modifier.size(24.dp))
            leading?.invoke()
            Box(Modifier.weight(1f).padding(vertical = 8.dp), contentAlignment = Alignment.CenterStart) {
                if (text.isEmpty()) Text(placeholder, style = Ab.type.body, color = if (enabled) colors.muted else colors.faint, maxLines = 2)
                BasicTextField(
                    value = text, onValueChange = { text = it }, enabled = enabled, modifier = Modifier.fillMaxWidth(),
                    textStyle = Ab.type.body.copy(color = colors.ink), cursorBrush = SolidColor(colors.ink), singleLine = true,
                    keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Done),
                    keyboardActions = KeyboardActions(onDone = { submit() }),
                )
            }
            if (text.isNotBlank()) {
                AbQuietIconButton(Icons.AutoMirrored.Outlined.KeyboardReturn, "Add", onClick = { submit() })
            } else if (onBrowse != null && enabled) {
                TextButton(onClick = onBrowse) { Text("From your lists", style = Ab.type.small, color = colors.muted, maxLines = 1) }
            } else {
                Box(Modifier.size(width = 4.dp, height = 44.dp))
            }
        }
    }
    }
}

private const val MIN_TYPED_FOR_SUGGESTIONS = 2
private const val MAX_SUGGESTIONS = 5
