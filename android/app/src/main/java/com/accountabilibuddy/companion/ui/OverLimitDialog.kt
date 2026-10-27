package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.OverLimitStore

/**
 * "Keep it to 3: park this for later?" (the PC's prompt for a 4th task, ADR 48): Park puts [title] under Come back to,
 * Add anyway adds it with a reason of at least [OverLimitStore.MIN_REASON_LENGTH] characters that stays on this phone.
 *
 * @param onPark Parks the held text; null when it already is a come-back (Park is then left out, Cancel keeps it parked).
 * @param onAddAnyway Called with the reason once it is long enough.
 */
@Composable
fun OverLimitDialog(title: String, onPark: (() -> Unit)?, onAddAnyway: (reason: String) -> Unit, onCancel: () -> Unit) {
    var reason by remember { mutableStateOf("") }
    var tooShort by remember { mutableStateOf(false) }
    AlertDialog(
        onDismissRequest = onCancel,
        title = {
            val question = if (onPark != null) "park \"$title\" for later?" else "leave \"$title\" parked?"
            Text("Keep it to ${OverLimitStore.MAX_TASKS}: $question")
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Or add it anyway and say why. The reason stays on this phone.", style = MaterialTheme.typography.bodyMedium)
                OutlinedTextField(
                    value = reason, onValueChange = { reason = it; tooShort = false }, modifier = Modifier.fillMaxWidth(),
                    label = { Text("Why add it anyway") },
                    isError = tooShort,
                    supportingText = if (tooShort) ({ Text("Write why, at least ${OverLimitStore.MIN_REASON_LENGTH} characters.") }) else null,
                )
            }
        },
        confirmButton = {
            Row {
                if (onPark != null) {
                    TextButton(onClick = onPark) { Text("Park") }
                }
                TextButton(onClick = {
                    if (reason.trim().length < OverLimitStore.MIN_REASON_LENGTH) tooShort = true else onAddAnyway(reason)
                }) { Text("Add anyway") }
            }
        },
        dismissButton = { TextButton(onClick = onCancel) { Text("Cancel") } },
    )
}
