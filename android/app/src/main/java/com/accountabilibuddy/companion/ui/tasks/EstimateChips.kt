package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.TaskFields
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.theme.Ab

private val StandardEstimates = listOf(25, 50, 90)

/**
 * The estimate chips (25, 50, 90 min), as on the PC: the chosen one is selected and a tap on it clears the estimate. A value of
 * its own (from the PC) shows as an extra chip, and "Other" asks for minutes.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun EstimateChips(minutes: Int?, onChange: (Int?) -> Unit, modifier: Modifier = Modifier) {
    var asking by remember { mutableStateOf(false) }
    // The chips wrap onto a second line when the text is large, so none is cut at the edge.
    FlowRow(modifier, horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(0.dp)) {
        StandardEstimates.forEach { value ->
            AbChip(TaskText.estimate(value), selected = minutes == value, onClick = { onChange(if (minutes == value) null else value) })
        }
        if (minutes != null && minutes !in StandardEstimates) {
            AbChip(TaskText.estimate(minutes), selected = true, onClick = { onChange(null) })
        }
        AbChip("Other", selected = false, onClick = { asking = true })
    }

    if (asking) {
        var typed by remember { mutableStateOf(minutes?.toString().orEmpty()) }
        val value = typed.trim().toIntOrNull()?.takeIf { it > 0 }
        val colors = Ab.colors
        AbDialog(
            title = "Estimate in minutes", onDismiss = { asking = false },
            confirmText = "Set", onConfirm = { if (value != null) { onChange(TaskFields.cleanEstimate(value)); asking = false } },
        ) {
            OutlinedTextField(
                value = typed, onValueChange = { typed = it.filter(Char::isDigit).take(4) }, singleLine = true,
                placeholder = { Text("e.g. 40", color = colors.faint) },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                colors = OutlinedTextFieldDefaults.colors(
                    focusedTextColor = colors.ink, unfocusedTextColor = colors.ink, focusedBorderColor = colors.ink,
                    unfocusedBorderColor = colors.line, cursorColor = colors.ink,
                ),
            )
        }
    }
}
