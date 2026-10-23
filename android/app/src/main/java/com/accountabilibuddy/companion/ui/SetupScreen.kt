package com.accountabilibuddy.companion.ui

import android.content.Context
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

/** The phone's first-run setup: every permission in one place, then into the app. Shown once; Settings → Permissions keeps the list. */
@Composable
fun SetupScreen(health: HealthStatus, onConnectHealth: () -> Unit, onAllowUsage: () -> Unit, onDone: () -> Unit) {
    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Text("Set up AccountabiliBuddy", style = MaterialTheme.typography.headlineSmall)
        Text(
            "Allow what you want the phone to do. Each one opens Android's own screen; come back here afterwards. " +
                "You can change any of them later in Settings → Permissions.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        PermissionsList(health, onConnectHealth, onAllowUsage)
        Button(onClick = onDone, modifier = Modifier.fillMaxWidth()) { Text("Done") }
        TextButton(onClick = onDone, modifier = Modifier.fillMaxWidth()) { Text("Skip for now") }
    }
}

/** Whether the first-run setup was shown (preferences "ui"). */
object SetupState {
    fun shown(context: Context): Boolean = context.getSharedPreferences("ui", Context.MODE_PRIVATE).getBoolean("setupShown", false)

    fun markShown(context: Context, shown: Boolean = true) {
        context.getSharedPreferences("ui", Context.MODE_PRIVATE).edit().putBoolean("setupShown", shown).apply()
    }
}
