package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab

/** A short explanation in a sheet, with one button to close it. */
@Composable
internal fun InfoSheet(title: String, text: String, onDismiss: () -> Unit) {
    AbSheet(onDismiss) {
        Text(title, style = Ab.type.head, color = Ab.colors.ink)
        Text(text, style = Ab.type.bodyMedium, color = Ab.colors.muted)
        AbTonalButton("Got it", onClick = onDismiss, modifier = Modifier.fillMaxWidth())
    }
}

/** One or more QR codes (a long sync file needs several) in a sheet, with what to do with them. */
@Composable
internal fun QrSheet(title: String, text: String, links: List<String>, onDismiss: () -> Unit) {
    AbSheet(onDismiss) {
        Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(12.dp), horizontalAlignment = Alignment.Start) {
            Text(title, style = Ab.type.head, color = Ab.colors.ink)
            Text(text, style = Ab.type.bodyMedium, color = Ab.colors.muted)
            links.forEachIndexed { index, link ->
                Image(QrPicture.render(link), contentDescription = "Code ${index + 1} of ${links.size}", modifier = Modifier.size(260.dp))
                if (links.size > 1) {
                    Text("${index + 1} of ${links.size}", style = Ab.type.small, color = Ab.colors.muted)
                }
            }
            AbTonalButton("Done", onClick = onDismiss, modifier = Modifier.fillMaxWidth())
        }
    }
}
