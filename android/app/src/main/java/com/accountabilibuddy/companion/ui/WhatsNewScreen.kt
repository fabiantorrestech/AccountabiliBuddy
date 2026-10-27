package com.accountabilibuddy.companion.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
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
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.setup.SetupFeatures

/**
 * The migration assistant on the phone: the setup pages of features added since the last version that aren't set
 * up yet (5 taps on the logo on About, or the Today card). Done marks them set up.
 */
@Composable
fun WhatsNewScreen(onConnectCurbox: () -> Unit, onReenableTime: (Boolean, java.time.LocalTime) -> String?, onClose: () -> Unit) {
    val context = LocalContext.current
    val pending = remember { SetupFeatures.pending(context) }
    BackHandler { onClose() }
    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("What's new", style = MaterialTheme.typography.headlineSmall, modifier = Modifier.weight(1f))
            TextButton(onClick = onClose) { Text("Close") }
        }
        if (pending.isEmpty()) {
            Text("Everything added since your last version is set up.", color = MaterialTheme.colorScheme.onSurfaceVariant)
            return@Column
        }

        pending.forEach { feature ->
            when (feature.id) {
                SetupFeatures.CURBOX -> CurboxSetupPage(onConnectCurbox, onReenableTime)
                SetupFeatures.BEDTIME -> BedtimeSetupPage()
                SetupFeatures.PLACEMENT -> PlacementSetupPage()
            }
        }
        Button(onClick = {
            pending.forEach { SetupFeatures.markDone(context, it.id) }
            onClose()
        }, modifier = Modifier.fillMaxWidth()) { Text("Done") }
    }
}

/** The quiet Today card after an upgrade that brought something to set up. */
@Composable
fun WhatsNewCard(appVersion: String, onSetUp: () -> Unit, onDismissed: () -> Unit) {
    val context = LocalContext.current
    val news = remember { SetupFeatures.newSinceUpgrade(context) }
    if (!SetupFeatures.showsCard(context, appVersion) || news.isEmpty()) return
    androidx.compose.material3.Card(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 6.dp)) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(if (news.size == 1) "New since your last version: ${news[0].title}" else "New since your last version: ${news.size} things to set up")
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = onSetUp) { Text("Set up") }
                TextButton(onClick = {
                    SetupFeatures.dismissCard(context, appVersion)
                    onDismissed()
                }) { Text("Not now") }
            }
        }
    }
}
