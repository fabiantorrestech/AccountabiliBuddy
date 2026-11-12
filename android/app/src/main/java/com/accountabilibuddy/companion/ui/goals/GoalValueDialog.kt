package com.accountabilibuddy.companion.ui.goals

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.text.input.KeyboardType
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbTextField

/**
 * A small dialog for one value by hand (Log a value…, Add time by hand…, a reading's day): the box takes a number of zero or more,
 * Save stays off until it does. [onSave] returns true when the value was taken (the dialog then closes).
 */
@Composable
fun GoalValueDialog(title: String, caption: String, onSave: (Double) -> Boolean, onDismiss: () -> Unit) {
    var text by rememberSaveable { mutableStateOf("") }
    val value = text.trim().replace(',', '.').toDoubleOrNull()?.takeIf { it >= 0 }
    AbDialog(
        title = title,
        onDismiss = onDismiss,
        confirmText = "Save",
        onConfirm = { if (value != null && onSave(value)) onDismiss() },
    ) {
        AbTextField(text, { text = it }, caption, keyboardType = KeyboardType.Decimal)
    }
}
