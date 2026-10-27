package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/**
 * The question a join link asks (ADR 22, 46). It says what would change, in this order: whether the phone leaves the
 * group it is in, which relay it would send its files to (the host, and a warning when that is a relay on the internet
 * or one this phone doesn't use now), and that the name in the link is only a label. A link that leaves the current
 * group asks a second time before it does anything.
 */
@Composable
internal fun JoinPromptDialog(prompt: JoinPrompt, onContinue: () -> Unit, onCancel: () -> Unit) {
    val link = prompt.link
    if (prompt.confirmingLeave) {
        AlertDialog(
            onDismissRequest = onCancel,
            title = { Text("Really leave your current group?") },
            text = {
                Text(
                    "Your other devices will stop syncing with this phone, and what came from them is removed from it. " +
                        "Only go on if you just made this link yourself, on your own device.",
                )
            },
            confirmButton = { TextButton(onClick = onContinue) { Text("Leave and join") } },
            dismissButton = { TextButton(onClick = onCancel) { Text("Keep my group") } },
        )
        return
    }

    AlertDialog(
        onDismissRequest = onCancel,
        title = { Text(if (prompt.leavesCurrentGroup) "Leave your current devices group?" else "Join a devices group?") },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState())) {
                if (prompt.leavesCurrentGroup) {
                    Text(
                        "You are in a devices group now. Joining this one leaves it: your other devices stop syncing with " +
                            "this phone, and what came from them is removed from it.",
                        fontWeight = FontWeight.SemiBold,
                    )
                } else {
                    Text(
                        "This phone joins a device group: it shares its sync file with the group's devices and carries theirs " +
                            "between them, encrypted with the link's code.",
                    )
                }

                Spacer(Modifier.height(8.dp))
                Text(
                    "The link calls itself \"${link.pcName}\". That name is only a label in the link, not proof of whose it is.",
                    style = MaterialTheme.typography.bodySmall,
                )
                link.relayHost?.let { host ->
                    Spacer(Modifier.height(8.dp))
                    Text("It also sends this phone's sync files to the relay at $host" + (if (link.relayPin != null) " (certificate pinned)." else "."))
                    if (prompt.relayOnInternet) {
                        Text(
                            "That is a relay on the internet, not on your home network.",
                            color = Color(0xFFFCA5A5),
                            fontWeight = FontWeight.SemiBold,
                        )
                    }

                    if (prompt.newRelay) {
                        Text(
                            "This phone doesn't use that relay now.",
                            color = Color(0xFFFCA5A5),
                        )
                    }
                }

                Spacer(Modifier.height(8.dp))
                Text(
                    "Only go on if you just made this link yourself, on your own device. A link from a message, a web page or " +
                        "someone else can move your data to their group and their server.",
                    style = MaterialTheme.typography.bodySmall,
                )
            }
        },
        confirmButton = {
            TextButton(onClick = onContinue) { Text(if (prompt.leavesCurrentGroup) "Continue" else "Join") }
        },
        dismissButton = { TextButton(onClick = onCancel) { Text("Cancel") } },
    )
}
