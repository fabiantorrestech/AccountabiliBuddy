package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import com.accountabilibuddy.companion.CompanionApp

/** Settings → Notification & widget → Timeline look: colour, grayscale (outlined) or e-ink (black and white). */
@Composable
internal fun TimelineLookSection() {
    val context = LocalContext.current
    val look by UiPrefs.barLook.collectAsState()
    Section("Timeline look", summary = look.label) {
        Text(
            "For a phone kept in grayscale, or an e-ink screen: every block outlined, and the now line ringed so it shows on anything.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        BarLook.entries.forEach { choice ->
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth().clickable {
                UiPrefs.setBarLook(context, choice)
                (context.applicationContext as CompanionApp).refreshSoon()
            }) {
                RadioButton(look == choice, {
                    UiPrefs.setBarLook(context, choice)
                    (context.applicationContext as CompanionApp).refreshSoon()
                })
                Text(choice.label)
            }
        }
    }
}
