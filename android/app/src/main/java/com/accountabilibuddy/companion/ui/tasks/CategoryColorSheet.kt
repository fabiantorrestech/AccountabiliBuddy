package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.TaskCategoryPresets

/**
 * A category's colour: 24 presets to start from, red, green and blue to adjust (a slider and a number each) and a hex box, as on the PC.
 * Nothing is kept until Done. Colours stay "#RRGGBB" (ADR 62).
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun CategoryColorSheet(name: String, color: String?, onDone: (String) -> Unit, onDismiss: () -> Unit) {
    val start = remember { rgbOf(color) ?: rgbOf(TaskCategoryPresets.PALETTE.first())!! }
    var red by remember { mutableIntStateOf(start.first) }
    var green by remember { mutableIntStateOf(start.second) }
    var blue by remember { mutableIntStateOf(start.third) }
    var hexText by remember { mutableStateOf(hexOf(start.first, start.second, start.third)) }
    var hexBad by remember { mutableStateOf(false) }
    val colors = Ab.colors

    fun show(rgb: Triple<Int, Int, Int>) {
        red = rgb.first
        green = rgb.second
        blue = rgb.third
        hexText = hexOf(rgb.first, rgb.second, rgb.third)
        hexBad = false
    }

    val draft = hexOf(red, green, blue)
    AbSheet(onDismiss = onDismiss) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(Modifier.size(28.dp).background(parseCategoryColor(draft) ?: colors.faint, CircleShape))
            Text(name, style = Ab.type.bodyMedium, color = colors.ink, maxLines = 1)
        }
        FlowRow(
            Modifier.fillMaxWidth().padding(16.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            TaskCategoryPresets.ALL.forEach { preset ->
                val picked = preset.equals(draft, ignoreCase = true)
                Box(
                    Modifier.size(36.dp)
                        .border(2.dp, if (picked) colors.ink else colors.bg.copy(alpha = 0f), CircleShape)
                        .padding(4.dp)
                        .background(parseCategoryColor(preset) ?: colors.faint, CircleShape)
                        .clickable { rgbOf(preset)?.let(::show) }
                        .semantics { contentDescription = preset },
                )
            }
        }
        ChannelRow("Red", red) { show(Triple(it, green, blue)) }
        ChannelRow("Green", green) { show(Triple(red, it, blue)) }
        ChannelRow("Blue", blue) { show(Triple(red, green, it)) }
        OutlinedTextField(
            value = hexText,
            onValueChange = { typed ->
                hexText = typed.take(7)
                val rgb = rgbOf(typed)
                hexBad = rgb == null
                if (rgb != null) {
                    red = rgb.first
                    green = rgb.second
                    blue = rgb.third
                }
            },
            singleLine = true,
            isError = hexBad,
            label = { Text("Hex") },
            supportingText = if (hexBad) ({ Text("A colour is six hex digits, like #2563EB.") }) else null,
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp),
        )
        Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), horizontalArrangement = Arrangement.End) {
            TextButton(onClick = onDismiss) { Text("Cancel", color = colors.muted) }
            TextButton(onClick = { onDone(draft) }) { Text("Done", color = colors.ink) }
        }
    }
}

@Composable
private fun ChannelRow(label: String, value: Int, onChange: (Int) -> Unit) {
    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(label, style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.width(44.dp))
        Slider(
            value = value.toFloat(), onValueChange = { onChange(it.toInt()) }, valueRange = 0f..255f,
            modifier = Modifier.weight(1f).semantics { contentDescription = label },
        )
        OutlinedTextField(
            value = value.toString(),
            onValueChange = { typed -> typed.filter(Char::isDigit).take(3).toIntOrNull()?.let { onChange(it.coerceIn(0, 255)) } },
            singleLine = true,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            modifier = Modifier.width(76.dp).semantics { contentDescription = "$label, 0 to 255" },
        )
    }
}

private fun hexOf(red: Int, green: Int, blue: Int): String = "#%02X%02X%02X".format(red, green, blue)

// Red, green and blue from what was typed (the # may be left out); null when it isn't six hex digits.
private fun rgbOf(text: String?): Triple<Int, Int, Int>? {
    val digits = text?.trim()?.removePrefix("#") ?: return null
    if (digits.length != 6) return null
    val rgb = digits.toIntOrNull(16) ?: return null
    return Triple((rgb shr 16) and 0xFF, (rgb shr 8) and 0xFF, rgb and 0xFF)
}
