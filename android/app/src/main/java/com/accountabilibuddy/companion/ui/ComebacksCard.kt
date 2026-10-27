package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Card
import androidx.compose.material3.Checkbox
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.OpenComebacks

/**
 * "Come back to" (ADR 30, 48): the user's parked thoughts, one list on every device. Ticking one or parking a new one
 * changes this phone's copy at once and reaches the PCs with the next sync, where the newest edit of each thought wins.
 */
@Composable
fun ComebacksCard(comebacks: OpenComebacks) {
    val app = LocalContext.current.applicationContext as CompanionApp
    var text by remember { mutableStateOf("") }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            IconTitle("Come back to", style = MaterialTheme.typography.titleSmall)
            if (comebacks.items.isEmpty()) {
                Text("Nothing parked.", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            comebacks.items.forEach { item ->
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(checked = false, onCheckedChange = { app.sharedLists.tickParked(item.id) })
                    Text(item.text, style = MaterialTheme.typography.bodyMedium)
                }
            }
            fun park() {
                val thought = text.trim()
                if (thought.isNotEmpty()) {
                    app.sharedLists.addParked(thought)
                    text = ""
                }
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = text, onValueChange = { text = it }, singleLine = true, modifier = Modifier.weight(1f),
                    placeholder = { Text("Park a thought") },
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                    keyboardActions = KeyboardActions(onDone = { park() }),
                )
                TextButton(onClick = { park() }, enabled = text.isNotBlank()) { Text("Park") }
            }
        }
    }
}
