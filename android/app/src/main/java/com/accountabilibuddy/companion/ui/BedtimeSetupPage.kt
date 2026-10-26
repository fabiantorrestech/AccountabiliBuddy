package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.essentials.EssentialsKind

/**
 * The setup page for the night (first-run setup and the migration assistant): the bedtime essentials, their screen
 * on unlocking at night and the LockMyPhone-Extended profile that locks the phone from bedtime until the wake time.
 * The same settings as Settings → Sleep → Bedtime essentials.
 */
@Composable
internal fun BedtimeSetupPage() {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("The night", style = MaterialTheme.typography.headlineSmall)
        Text(
            "From bedtime until your wake time the phone can have its own essentials: a short list for the night (the alarm, " +
                "calls) on a black screen when you unlock it, and LockMyPhone-Extended can lock everything else. " +
                "Bedtime and wake come from Sleep times and your day plan. All of it is in Settings → Sleep later.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        EssentialsSection(EssentialsKind.Bedtime, startOpen = true)
    }
}
