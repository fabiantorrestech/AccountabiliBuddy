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
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

/**
 * The phone's first-run setup: every permission in one place, then Curbox (what to make there, allowing
 * AccountabiliBuddy, when blocks come back on, hard mode), then the night (bedtime essentials and a
 * LockMyPhone-Extended lock for it), then into the app. Shown once; Settings → Permissions &
 * setup keeps the list.
 */
@Composable
fun SetupScreen(
    health: HealthStatus,
    onConnectHealth: () -> Unit,
    onAllowUsage: () -> Unit,
    onConnectCurbox: () -> Unit = {},
    onReenableTime: (Boolean, java.time.LocalTime) -> String? = { _, _ -> null },
    onDone: () -> Unit,
) {
    val context = androidx.compose.ui.platform.LocalContext.current
    var page by androidx.compose.runtime.saveable.rememberSaveable { androidx.compose.runtime.mutableIntStateOf(0) }
    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        if (page == 1) {
            CurboxSetupPage(onConnectCurbox, onReenableTime)
            Button(onClick = {
                com.accountabilibuddy.companion.setup.SetupFeatures.markDone(context, com.accountabilibuddy.companion.setup.SetupFeatures.CURBOX)
                page = 2
            }, modifier = Modifier.fillMaxWidth()) { Text("Next: the night") }
            TextButton(onClick = { page = 0 }, modifier = Modifier.fillMaxWidth()) { Text("Back") }
            return@Column
        }

        if (page == 2) {
            BedtimeSetupPage()
            Button(onClick = {
                com.accountabilibuddy.companion.setup.SetupFeatures.markDone(context, com.accountabilibuddy.companion.setup.SetupFeatures.BEDTIME)
                onDone()
            }, modifier = Modifier.fillMaxWidth()) { Text("Done") }
            TextButton(onClick = { page = 1 }, modifier = Modifier.fillMaxWidth()) { Text("Back") }
            return@Column
        }

        Text("Set up AccountabiliBuddy", style = MaterialTheme.typography.headlineSmall)
        Text(
            "Allow what you want the phone to do. Each one opens Android's own screen; come back here afterwards. " +
                "You can change any of them later in Settings → Permissions & setup.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        PermissionsList(health, onConnectHealth, onAllowUsage)
        Button(onClick = { page = 1 }, modifier = Modifier.fillMaxWidth()) { Text("Next: Curbox") }
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
