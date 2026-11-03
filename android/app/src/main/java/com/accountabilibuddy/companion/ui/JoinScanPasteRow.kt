package com.accountabilibuddy.companion.ui

import android.content.ClipboardManager
import android.content.Context
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ContentPaste
import androidx.compose.material.icons.outlined.QrCodeScanner
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.SyncKeyCode

/**
 * "Scan" and "Paste" next to the typed link code (ADR 57). Both only produce text and give it to the join path that already exists:
 * [onLink] takes a join link (the caller offers the usual "Join <name>'s devices?" question) and says whether it was one; a bare
 * code goes to [onCode], which fills the code field, so the user still taps Join. Anything else is refused. The code is never logged.
 */
@Composable
internal fun JoinScanPasteRow(onLink: (String) -> Boolean, onCode: (String) -> Unit, enabled: Boolean = true) {
    val context = LocalContext.current
    var scanning by rememberSaveable { mutableStateOf(false) }
    var pasteNote by remember { mutableStateOf<String?>(null) }

    val accept = { text: String -> acceptJoinText(text, onLink, onCode) }

    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            AbTonalButton("Scan", onClick = { pasteNote = null; scanning = true }, enabled = enabled, icon = Icons.Outlined.QrCodeScanner, modifier = Modifier.weight(1f))
            AbTonalButton("Paste", onClick = { pasteNote = pasteFromClipboard(context, accept) }, enabled = enabled, icon = Icons.Outlined.ContentPaste, modifier = Modifier.weight(1f))
        }
        pasteNote?.let { Text(it, style = Ab.type.small, color = Ab.colors.drift) }
    }

    if (scanning) {
        JoinScanSheet(
            onText = accept,
            onPaste = {
                scanning = false
                pasteNote = pasteFromClipboard(context, accept)
            },
            onClose = { scanning = false },
        )
    }
}

/** True when [text] was a join link or a link code (the same checks as a link opened from outside and the typed field). */
private fun acceptJoinText(text: String, onLink: (String) -> Boolean, onCode: (String) -> Unit): Boolean {
    val trimmed = text.trim()
    if (onLink(trimmed)) {
        return true
    }

    if (SyncKeyCode.parse(trimmed) != null) {
        onCode(trimmed)
        return true
    }

    return false
}

/** Reads the clipboard now (only called from the Paste button). Returns the line to show when nothing usable was there, else null. */
private fun pasteFromClipboard(context: Context, accept: (String) -> Boolean): String? {
    val clipboard = context.getSystemService(ClipboardManager::class.java)
    val text = clipboard?.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(context)?.toString()
    return when {
        text.isNullOrBlank() -> "The clipboard is empty."
        accept(text) -> null
        else -> "The clipboard doesn't hold a link code from your devices."
    }
}
