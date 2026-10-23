package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.OpenComebacks

/** "Come back to" (ADR 30): the PC's parked thoughts, read-only; ticked on the PC. */
@Composable
fun ComebacksCard(comebacks: OpenComebacks) {
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            IconTitle("Come back to · ${comebacks.source}", style = MaterialTheme.typography.titleSmall)
            comebacks.items.forEach { item -> Text("• ${item.text}", style = MaterialTheme.typography.bodyMedium) }
        }
    }
}
