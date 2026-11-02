package com.accountabilibuddy.companion.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.LinkOff
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.setup.StartOver
import com.accountabilibuddy.companion.sync.UnlinkedState
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/** The three things the Unlinked screen can be showing. */
private enum class UnlinkedMode { Choices, AddAgain, StartOver }

/**
 * The full-screen state of a phone that was removed from its synced devices, or cut off by a removal (ADR 54): it syncs nothing until
 * the user picks one of three things, with no default and no way back into a syncing screen (Back does not leave this state):
 * - Add this phone again: the join flow (scan the other device's QR with the camera, or type its link code), the same device id.
 * - Continue unsynced here: keeps all data, including what it received; the Devices tab can link again.
 * - Start from scratch: only when no focus session the user started is running; a typed "start over" keeps a private local copy of
 *   the app's files, then clears the app and restarts into setup.
 * Every choice, adding again included, waits while a focus session the user started runs (as on the PC).
 *
 * Minimal and functional; it gets restyled in the phone UI run. [lockedReason] is the focus-session lock as text.
 */
@Composable
fun UnlinkedScreen(
    state: UnlinkedState,
    lockedReason: String?,
    onJoin: (String) -> Boolean,
    onContinueUnsynced: () -> Unit,
    onStartFromScratch: (String) -> Unit,
) {
    var mode by rememberSaveable { mutableStateOf(UnlinkedMode.Choices) }
    // This is the whole app while it lasts: Back on the two sub-pages returns to the choices, and on the choices it only puts the app
    // away; it never reaches a syncing screen.
    val activity = LocalContext.current as? android.app.Activity
    BackHandler(enabled = true) {
        if (mode != UnlinkedMode.Choices) mode = UnlinkedMode.Choices else activity?.moveTaskToBack(true)
    }

    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Icon(Icons.Outlined.LinkOff, contentDescription = null, modifier = Modifier.size(40.dp), tint = MaterialTheme.colorScheme.error)
        Text(
            if (state.removed) "This phone was removed from your synced devices" else "This phone was cut off from your synced devices",
            style = MaterialTheme.typography.headlineSmall,
        )
        Text(
            "by ${state.removedByName} on ${AT_FORMAT.format(state.at.atZone(ZoneId.systemDefault()))}",
            style = MaterialTheme.typography.bodyLarge,
        )
        if (!state.removed) {
            Text(
                "It couldn't get the new key, most likely because this app is older than the removing device's.",
                style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }

        Text(
            "Nothing syncs from this phone until you choose. What it already received stays on it.",
            style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )

        when (mode) {
            UnlinkedMode.Choices -> Choices(lockedReason, onChoose = { mode = it }, onContinueUnsynced)
            UnlinkedMode.AddAgain -> AddAgain(lockedReason, onJoin, onBack = { mode = UnlinkedMode.Choices })
            UnlinkedMode.StartOver -> StartOverPage(lockedReason, onStartFromScratch, onBack = { mode = UnlinkedMode.Choices })
        }
    }
}

@Composable
private fun Choices(lockedReason: String?, onChoose: (UnlinkedMode) -> Unit, onContinueUnsynced: () -> Unit) {
    Choice(
        "Add this phone again",
        "Link it again with a code or QR from a device that is still linked. It keeps its own history and settings; what it received from the other devices is fetched again.",
        enabled = lockedReason == null,
    ) { onChoose(UnlinkedMode.AddAgain) }
    Choice(
        "Continue unsynced here",
        "Keep everything on this phone, including what it received, and stop syncing. You can link again from Settings › Devices & sync.",
        enabled = lockedReason == null,
        onClick = onContinueUnsynced,
    )
    Choice(
        "Start from scratch",
        "Clear everything AccountabiliBuddy keeps on this phone and set it up again as new.",
        enabled = lockedReason == null,
    ) { onChoose(UnlinkedMode.StartOver) }
    lockedReason?.let {
        Text("Locked: $it Every choice here waits until it ends.", color = MaterialTheme.colorScheme.error)
    }
}

// A button and what it does. All three look alike: none is the default.
@Composable
private fun Choice(title: String, detail: String, enabled: Boolean, onClick: () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        OutlinedButton(onClick = onClick, enabled = enabled, modifier = Modifier.fillMaxWidth()) { Text(title) }
        Text(detail, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun AddAgain(lockedReason: String?, onJoin: (String) -> Boolean, onBack: () -> Unit) {
    var code by rememberSaveable { mutableStateOf("") }
    var wrong by remember { mutableStateOf(false) }
    Text(
        "On a device that is still linked, open Settings › Devices & sync › Link a device. Scan its QR with this phone's camera (it opens this app), " +
            "or type its link code here.",
    )
    OutlinedTextField(
        value = code,
        onValueChange = {
            code = it
            wrong = false
        },
        label = { Text("Link code (40 characters)") },
        singleLine = true,
        isError = wrong,
        modifier = Modifier.fillMaxWidth(),
    )
    if (wrong) {
        Text("That isn't a link code from your devices.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
    }

    lockedReason?.let { Text("Locked: $it Adding this phone again waits until it ends.", color = MaterialTheme.colorScheme.error) }
    Button(onClick = { wrong = !onJoin(code) }, enabled = code.isNotBlank() && lockedReason == null, modifier = Modifier.fillMaxWidth()) {
        Text("Add this phone again")
    }
    TextButton(onClick = onBack, modifier = Modifier.fillMaxWidth()) { Text("Back to the choices") }
}

@Composable
private fun StartOverPage(lockedReason: String?, onStartFromScratch: (String) -> Unit, onBack: () -> Unit) {
    var typed by rememberSaveable { mutableStateOf("") }

    Text("This clears everything AccountabiliBuddy keeps on this phone: settings, timers, the link to your other devices and what it received from them. Setup then opens as on a first run.")
    Text(
        "A copy of the app's files is kept first, in the app's own private storage on this phone (the three newest copies stay; " +
            "uninstalling the app removes them). It is never sent anywhere.",
    )
    Text(
        "Not touched: Curbox and your other apps, Android's permissions, your Google sign-in (remove it in your Google account if you want), and your other devices.",
        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    Text("If this phone has a Pixel Watch linked, the watch is told and asks whether to forget its data.", style = MaterialTheme.typography.bodySmall)
    lockedReason?.let { Text("Locked: $it", color = MaterialTheme.colorScheme.error) }
    OutlinedTextField(
        value = typed,
        onValueChange = { typed = it },
        label = { Text("Type \"${StartOver.PHRASE}\" to confirm") },
        singleLine = true,
        enabled = lockedReason == null,
        modifier = Modifier.fillMaxWidth(),
    )
    Button(
        onClick = { onStartFromScratch(typed) },
        enabled = lockedReason == null && typed.trim().equals(StartOver.PHRASE, ignoreCase = true),
        modifier = Modifier.fillMaxWidth(),
    ) { Text("Clear everything and restart") }
    TextButton(onClick = onBack, modifier = Modifier.fillMaxWidth()) { Text("Back to the choices") }
}

// E.g. "Oct 28, 3:20 PM": 12-hour with AM/PM, whatever the system's 24-hour setting.
private val AT_FORMAT: DateTimeFormatter = DateTimeFormatter.ofPattern("MMM d, h:mm a", Locale.US)
