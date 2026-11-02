package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.DriveFolderSource
import com.accountabilibuddy.companion.sync.DriveSyncSource
import com.accountabilibuddy.companion.sync.FolderStatus
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Duration
import java.time.Instant

// The three sync methods' own settings on Devices & sync (the switches are in "How they sync"): what each last did, and what it needs.

/** "Synced 3 min ago", the method's problem, or "Not synced yet.", as one small line (a problem in the drift colour). */
@Composable
internal fun MethodStatusLine(status: FolderStatus, now: Instant) {
    Text(
        status.problem ?: status.lastSyncAt?.let { "Synced ${DevicesModel.ago(Duration.between(it, now))}" } ?: "Not synced yet.",
        style = Ab.type.small,
        color = if (status.problem != null) Ab.colors.drift else Ab.colors.muted,
    )
}

/** Google Drive (ADR 26): the app's own Drive folder, the one the PCs use; off until signed in. */
@Composable
internal fun DriveMethodCard(devices: DevicesState, actions: DevicesActions, now: Instant, signIn: () -> Unit) {
    val status = devices.drive
    AbCard(verticalSpacing = 8.dp) {
        AbSectionLabel("Google Drive")
        if (devices.settings.driveOn) {
            MethodStatusLine(status, now)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                AbTonalButton("Sync now", onClick = actions.onSyncDriveNow)
                if (status.problem == DriveSyncSource.SIGN_IN_AGAIN) {
                    AbTonalButton("Sign in again", onClick = signIn)
                }
            }
        } else {
            Text(
                "Sync through the app's own Drive folder, the one your PCs use, at home, at work or on mobile data. The app sees only the files it made.",
                style = Ab.type.small, color = Ab.colors.muted,
            )
        }
        // The steps are what's usually missing while Drive isn't on yet (or sign-in failed).
        DriveSetupHelp(startOpen = false)
    }
}

/** The short steps for a shared folder kept in step through the user's own server; the full guide is on the PC. */
private val SELF_HOSTED_STEPS = """
    1. Install Syncthing-Fork here and Syncthing on your computers. Or, with Nextcloud, a two-way sync app such as Autosync for Nextcloud or FolderSync (Nextcloud's own auto upload only uploads).
    2. Connect the devices (Syncthing: Show ID on one, Add Remote Device on the other) and let the app ignore battery optimisation.
    3. Share one folder, e.g. "AccountabiliBuddy sync", as Send & Receive. On the phone keep it in internal storage, not the top of Download.
    4. Here: switch on Shared folder and pick that folder in Android's folder picker.
    5. On the PC: Devices → Shared folder → choose the same folder.
    6. Away from home, let Syncthing's relays or a mesh VPN (Tailscale, WireGuard) carry it. Never open a file share to the internet.
    The files are sealed, so the tool and any server only carry scrambled bytes. The full guide (always-on computer, safe access from outside, a small rented server): on the PC, Devices → Sync with your own server → Open the full guide.
""".trimIndent()

/** The shared folder (a folder another app such as Autosync keeps in step with Google Drive or a server). */
@Composable
internal fun FolderMethodCard(devices: DevicesState, actions: DevicesActions, now: Instant, onChoose: () -> Unit) {
    val settings = devices.settings
    AbCard(verticalSpacing = 8.dp) {
        AbSectionLabel("Shared folder")
        Hint(SELF_HOSTED_STEPS, label = "Sync with your own server (Syncthing, Nextcloud)")
        if (settings.folderOn) {
            Text(
                settings.folderUri?.let { "Folder: ${DriveFolderSource.displayName(it)}" } ?: "No folder chosen yet.",
                style = Ab.type.bodyMedium, color = Ab.colors.ink,
            )
            AbChipRow {
                listOf(1, 5, 15).forEach { minutes ->
                    AbChip("$minutes min", selected = settings.folderMinutes == minutes, onClick = { actions.onSettings(settings.copy(folderMinutes = minutes)) })
                }
            }
            MethodStatusLine(devices.folder, now)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                AbTonalButton("Choose folder", onClick = onChoose)
                AbTonalButton("Sync now", onClick = actions.onSyncFolderNow, enabled = settings.folderUri != null)
            }
        }
    }
}

/** The short steps for the phone's own relay; the full guide is on the PC (Devices -> Own relay). */
internal val RELAY_STEPS = """
    1. Run the relay on a PC, a home server or a rented server (the PC's Devices page has the steps and the full guide).
    2. Easiest: on a device that already uses the relay, show its link QR (Link a device) and scan it here: the address, pin and admission key come with it. Never photograph or post that QR: it is the key to all your synced data.
    3. Or type the relay's https address here, its pin (43 characters) when the relay made its own certificate, and its admission key (43 characters, the relay prints it; it lets this phone start the group on the relay). No pin: the relay needs a normal certificate (a reverse proxy with Let's Encrypt).
    4. Switch it on. This phone checks the pin on every connection and shows a problem instead of connecting if it doesn't match.
    The relay stores only sealed files and your link code never reaches it. While a session runs or the timeline is pinned, a change arrives in about a second; otherwise this phone looks every 15 minutes.
""".trimIndent()

/**
 * The user's own sealed relay (ADR 46): its https address, pin and admission key. The fields are always here, because the switch
 * in "How they sync" reads them ([relayFields]) when it is turned on.
 */
@Composable
internal fun RelayMethodCard(devices: DevicesState, actions: DevicesActions, now: Instant, fields: RelayFields) {
    val settings = devices.settings
    AbCard(verticalSpacing = 8.dp) {
        AbSectionLabel("Own relay")
        AbTextField(fields.address, { fields.address = it }, "Relay address (https://...)", capitalization = KeyboardCapitalization.None)
        AbTextField(fields.pin, { fields.pin = it }, "Certificate pin (empty: normal certificate)", capitalization = KeyboardCapitalization.None)
        AbTextField(
            fields.admission, { fields.admission = it }, "Admission key (from the relay)",
            capitalization = KeyboardCapitalization.None, visualTransformation = PasswordVisualTransformation(),
        )
        fields.error?.let { Text(it, style = Ab.type.small, color = Ab.colors.drift) }
        if (settings.relayOn && fields.changed(settings)) {
            AbTonalButton("Use this address, pin and key", onClick = { fields.checked(settings)?.let { actions.onSettings(it) } })
        }
        Hint(RELAY_STEPS, label = "Sync through your own relay")
        if (settings.relayOn) {
            MethodStatusLine(devices.relay, now)
            AbTonalButton("Sync now", onClick = actions.onSyncRelayNow)
        }
    }
}
