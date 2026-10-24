package com.accountabilibuddy.companion.ui

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.pm.PackageManager
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import java.security.MessageDigest

/**
 * How to let this phone sign in to Google Drive (ADR 26), under the Drive switch: no client ID or secret is pasted on
 * the phone; Google recognises the app by its package name and signing key, registered as an Android client in the
 * same Google Cloud project the PC uses. Both values are shown with Copy (the SHA-1 read from this build's own key).
 */
@Composable
internal fun DriveSetupHelp(startOpen: Boolean) {
    val context = LocalContext.current
    var open by remember { mutableStateOf(startOpen) }
    val sha1 = remember { signingSha1(context) }
    TextButton(onClick = { open = !open }) { Text(if (open) "How to set up Google Drive ▾" else "How to set up Google Drive ▸") }
    if (!open) return

    Column(Modifier.padding(start = 8.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(
            "No client ID or secret goes on the phone. Google recognises this app by its package name and signing key. In the same Google Cloud project your PC uses (its Google setup guide made it):",
            style = MaterialTheme.typography.bodySmall,
        )
        Step("1", "APIs & Services → Library → Google Drive API → Enable.")
        Step("2", "Google Auth Platform → Data access → Add or remove scopes → add …/auth/drive.file → Save.")
        Step("3", "Audience → Test users → add the Google account you'll pick here (not needed once the app is published).")
        Step("4", "Clients → Create client → Application type: Android, name \"AccountabiliBuddy phone\", then these two values → Create. Nothing to copy back.")
        CopyRow(context, "Package name", context.packageName)
        sha1?.let { CopyRow(context, "SHA-1 certificate fingerprint", it) }
        Step("5", "Turn on Google Drive above and pick that account. \"Wasn't allowed\" means one of 1–4 is missing or in another project; a new client can take a few minutes.")
    }
}

@Composable
private fun Step(number: String, text: String) {
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("$number.", style = MaterialTheme.typography.bodySmall)
        Text(text, style = MaterialTheme.typography.bodySmall)
    }
}

@Composable
private fun CopyRow(context: Context, label: String, value: String) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(label, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text(value, style = MaterialTheme.typography.bodySmall, fontFamily = FontFamily.Monospace)
        }
        TextButton(onClick = {
            context.getSystemService(ClipboardManager::class.java)?.setPrimaryClip(ClipData.newPlainText(label, value))
        }) { Text("Copy") }
    }
}

/** This build's signing certificate's SHA-1, "AA:BB:…"; null when it can't be read. */
private fun signingSha1(context: Context): String? = runCatching {
    val info = context.packageManager.getPackageInfo(context.packageName, PackageManager.GET_SIGNING_CERTIFICATES)
    val certificate = info.signingInfo!!.apkContentsSigners.first().toByteArray()
    MessageDigest.getInstance("SHA-1").digest(certificate).joinToString(":") { "%02X".format(it) }
}.getOrNull()
