package com.accountabilibuddy.companion.ui

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
import com.accountabilibuddy.companion.essentials.LockMyPhoneClient
import com.accountabilibuddy.companion.essentials.LockMyPhoneInstaller
import com.accountabilibuddy.companion.essentials.LockMyPhoneTrust
import com.accountabilibuddy.companion.essentials.LockScreen
import com.accountabilibuddy.companion.essentials.LockScreenChoice
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlinx.coroutines.launch
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * "How your phone locks" (ADR 73, 74): LockMyPhone-Extended or the built-in essentials screen, for every place the
 * essentials engage. Without LockMyPhone-Extended it recommends it with a Download button (then Android's installer,
 * then an offer to delete the downloaded file) and links to the apps page and its source; an outdated one gets Update.
 * Switching to the built-in screen waits the looser-change time and never during a session. Settings and the setup
 * both show it.
 */
@Composable
internal fun LockChoiceCard() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val scope = rememberCoroutineScope()
    // Read again whenever the page comes back (after Android's installer or the "install unknown apps" page).
    var reads by remember { mutableIntStateOf(0) }
    val lifecycleOwner = LocalLifecycleOwner.current
    DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME) {
                LockMyPhoneClient.forgetCapabilities()
                reads++
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }
    val installed = remember(reads) { EssentialsLaunch.lockMyPhoneInstalled(context) }
    val notTheRealOne = remember(reads) { !installed && LockMyPhoneTrust.present(context) }
    val choice = remember(reads) { app.lockScreen.chosen() }
    val pendingAt = remember(reads) { app.lockScreen.pendingBuiltInAt() }
    val needsUpdate = remember(reads) { LockMyPhoneClient.needsUpdate(context) }
    val downloaded = remember(reads) { LockMyPhoneInstaller.downloaded(context) != null }
    var busy by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf<String?>(null) }
    var askDelete by remember { mutableStateOf(true) }

    fun downloadAndInstall() {
        busy = true
        message = "Downloading LockMyPhone-Extended…"
        scope.launch {
            when (val outcome = LockMyPhoneInstaller.download(context)) {
                LockMyPhoneInstaller.Outcome.Ready -> {
                    message = if (LockMyPhoneInstaller.install(context)) null
                    else "Allow this app to install apps on the page that opened, then come back and tap Install."
                }
                is LockMyPhoneInstaller.Outcome.Failed -> message = outcome.message
            }
            busy = false
            reads++
        }
    }

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        AbSectionLabel("How your phone locks")
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            GuardChip("LockMyPhone-Extended", selected = choice == LockScreen.LockMyPhone, onClick = {
                app.lockScreen.chooseLockMyPhone()
                reads++
            })
            GuardChip("Built-in screen", selected = choice == LockScreen.BuiltIn && pendingAt == null, onClick = {
                message = if (app.lockScreen.requestBuiltIn()) null else "Not during a session: it's a looser change."
                reads++
            })
        }
        Note(
            if (choice == LockScreen.LockMyPhone) {
                "LockMyPhone-Extended really locks the phone during a session (no early unlock while it runs) and at night; this app's " +
                    "essentials screen never shows. Your essentials, folders, Phone corner and icons are sent to its \"Accountabilibuddy\" profiles."
            } else {
                "This app's essentials screen: soft, nothing is blocked, but Back and Home bring it back during a session."
            },
        )
        pendingAt?.let {
            Note("Switching to the built-in screen at ${TIME.format(it.atZone(ZoneId.systemDefault()))} (a looser change waits ${LockScreenChoice.BUILT_IN_WAIT.toMinutes()} minutes).")
        }
        when {
            !installed -> {
                if (notTheRealOne) {
                    // Android refuses an update from a different signer, so a download can't replace it: no button until it is gone.
                    Note(
                        "An app with LockMyPhone-Extended's name is installed, but it isn't signed with this app's key, so it isn't used. " +
                            "Uninstall it first, then come back here to download the right one.",
                        color = Ab.colors.drift,
                    )
                } else {
                    Note("LockMyPhone-Extended isn't installed. It's recommended: it gives far more control than the built-in screen.", color = Ab.colors.waiting)
                    GuardTonalButton(if (downloaded) "Install LockMyPhone-Extended" else "Download LockMyPhone-Extended", enabled = !busy, onClick = {
                        if (downloaded) {
                            message = if (LockMyPhoneInstaller.install(context)) null else "Allow this app to install apps, then tap Install again."
                        } else {
                            downloadAndInstall()
                        }
                    }, modifier = Modifier.fillMaxWidth())
                }
            }
            needsUpdate -> {
                Note("Your LockMyPhone-Extended is older than this app needs for strict session locks.", color = Ab.colors.waiting)
                GuardTonalButton("Update LockMyPhone-Extended", enabled = !busy, onClick = ::downloadAndInstall, modifier = Modifier.fillMaxWidth())
            }
        }
        message?.let { Note(it) }
        Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            TextButton(onClick = { open(context, LockMyPhoneInstaller.APPS_PAGE_URL) }) { Text("The apps page") }
            TextButton(onClick = { open(context, LockMyPhoneInstaller.SOURCE_URL) }) { Text("Its source code") }
        }
    }

    // Installed now: offer to delete the downloaded file (ADR 74).
    if (installed && !needsUpdate && downloaded && askDelete) {
        AlertDialog(
            onDismissRequest = { askDelete = false },
            title = { Text("LockMyPhone-Extended is installed") },
            text = { Text("Delete the downloaded installer? It isn't needed any more.") },
            confirmButton = {
                TextButton(onClick = {
                    LockMyPhoneInstaller.deleteDownloaded(context)
                    askDelete = false
                    reads++
                }) { Text("Delete") }
            },
            dismissButton = { TextButton(onClick = { askDelete = false }) { Text("Keep it") } },
        )
    }
}

private val TIME: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

private fun open(context: Context, url: String) {
    runCatching { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
}
