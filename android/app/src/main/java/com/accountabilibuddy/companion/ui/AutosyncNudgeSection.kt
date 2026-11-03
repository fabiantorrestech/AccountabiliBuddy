package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.AutosyncNudger
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.AbToggleRow
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab

private const val AUTOSYNC_STEPS = "In Autosync: Settings → Automation: switch it on and set a Secret code. Type the same code here. " +
    "Then, around a lunch, this app tells Autosync to sync instead of waiting for its own schedule. Nothing leaves this phone because of it; " +
    "Autosync does its own syncing. The code stays on this phone (not in the sync file, not in a backup)."

/** "Tell Autosync to sync" (under the shared folder): off until set up, a secret code, and a Test that only says it sent the message. */
@Composable
internal fun AutosyncNudgeSection() {
    val nudger = (LocalContext.current.applicationContext as CompanionApp).autosyncNudger
    val enabled by nudger.enabled.collectAsState()
    val hasCode by nudger.hasCode.collectAsState()
    var code by remember { mutableStateOf("") }
    var result by remember { mutableStateOf<String?>(null) }
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        AbToggleRow(
            "Tell Autosync to sync", enabled, { nudger.save(it) },
            subtitle = "Around a lunch, so the other devices hear of it sooner. Off until you set it up.",
        )
        if (enabled || hasCode) {
            AbTextField(
                code, { code = it }, if (hasCode) "Secret code (saved: type a new one to change)" else "Autosync's secret code",
                capitalization = KeyboardCapitalization.None, visualTransformation = PasswordVisualTransformation(),
            )
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                AbTonalButton("Save code", onClick = {
                    nudger.save(enabled, code)
                    code = ""
                    result = null
                }, enabled = code.isNotBlank())
                AbTonalButton("Test", onClick = {
                    result = when (nudger.send()) {
                        AutosyncNudger.Result.Sent -> "Message sent to Autosync. This can't tell if Autosync got it: look for a sync starting there."
                        AutosyncNudger.Result.NoCode -> "Save the secret code first."
                        AutosyncNudger.Result.NotInstalled -> "Autosync is not installed on this phone."
                        AutosyncNudger.Result.Failed -> "The message could not be sent."
                    }
                }, enabled = hasCode)
            }
            if (!nudger.isAutosyncInstalled()) {
                Text("Autosync is not installed on this phone.", style = Ab.type.small, color = Ab.colors.muted)
            }

            result?.let { Text(it, style = Ab.type.small, color = Ab.colors.muted) }
        }

        Hint(AUTOSYNC_STEPS, label = "Turn on Automation in Autosync")
    }
}
