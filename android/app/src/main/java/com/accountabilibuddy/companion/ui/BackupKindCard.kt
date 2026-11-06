package com.accountabilibuddy.companion.ui

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.backup.PhoneKindBackup
import com.accountabilibuddy.companion.data.BackupFile
import com.accountabilibuddy.companion.data.ImportException
import com.accountabilibuddy.companion.sync.DriveBackupInfo
import com.accountabilibuddy.companion.sync.DriveFiles
import com.accountabilibuddy.companion.sync.DriveNetwork
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonObject
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * One kind of backup on Settings → Backup (ADR 71): export, import (Replace or Merge), its auto-backup switch, and the sealed copies on
 * Google Drive to restore from. "This phone's settings" imports through [rememberBackupRestore] (it chooses what comes back and restarts).
 */
@Composable
internal fun BackupKindCard(kind: PhoneKindBackup.Kind, importLocked: String?, folderChosen: Boolean, onMessage: (String) -> Unit) {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val scope = rememberCoroutineScope()
    var autoOn by remember { mutableStateOf(app.autoBackup.switchOf(kind)) }
    var lockedFile by remember { mutableStateOf<String?>(null) }
    var password by remember { mutableStateOf("") }
    var opened by remember { mutableStateOf<JsonObject?>(null) }
    var driveList by remember { mutableStateOf<List<DriveBackupInfo>?>(null) }
    val shared = app.sync.sharesTaskLists()
    val showsImport = kind != PhoneKindBackup.Kind.Tasks || !shared

    fun open(content: JsonObject) {
        val problem = PhoneKindBackup.problem(app, content, kind)
        if (problem != null) onMessage(problem) else opened = content
    }

    val export = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/octet-stream")) { uri ->
        if (uri != null) {
            onMessage(
                runCatching {
                    context.contentResolver.openOutputStream(uri)?.use { it.write(PhoneKindBackup.export(app, kind).toByteArray()) }
                    "${kind.title} exported."
                }.getOrElse { "Couldn't export: ${it.message}" },
            )
        }
    }
    val deviceRestore = rememberBackupRestore(onMessage = onMessage)
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        val text = uri?.let { runCatching { context.contentResolver.openInputStream(it)?.use { stream -> stream.readBytes().decodeToString() } }.getOrNull() }
        when {
            uri == null -> Unit
            text == null || !BackupFile.isBackup(text) -> onMessage("That isn't a ${kind.title.lowercase()} backup.")
            BackupFile.needsPassword(text) -> lockedFile = text
            else -> runCatching { open(BackupFile.open(text)) }.onFailure { onMessage(it.message ?: "Couldn't open it.") }
        }
    }

    AbSectionLabel(kind.title)
    Note(
        when (kind) {
            PhoneKindBackup.Kind.Synced -> "Profiles, sleep and bedtime reminders, the day bar and the mode" + (if (shared) ", and your task lists." else ".") + " The same on every device."
            PhoneKindBackup.Kind.Device -> "Everything set on this phone alone (both modes' copies), timers, Curbox settings and guard history. Restores on a phone only."
            PhoneKindBackup.Kind.Tasks -> "Tasks, Later, come-backs and task categories." + if (shared) " They also travel in the synced settings while they're shared." else " Kept on this phone only, as they aren't shared."
        },
    )
    if (showsImport && folderChosen) {
        ToggleRow("Auto-backup", "Back up ${kind.title.lowercase()} to the folder (and sealed to Google Drive with Drive sync on) when they change.", autoOn) {
            autoOn = it
            app.autoBackup.setSwitch(kind, it)
        }
    }
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        AbTonalButton("Export…", onClick = {
            export.launch("AccountabiliBuddy ${kind.title} ${LocalDate.now()}${kind.extension}")
        })
        if (showsImport) {
            AbTonalButton("Import…", enabled = importLocked == null, onClick = {
                if (kind == PhoneKindBackup.Kind.Device) deviceRestore() else picker.launch(arrayOf("*/*"))
            })
            if (kind != PhoneKindBackup.Kind.Device && app.sync.settings.value.driveOn && app.sync.isJoined) {
                AbTonalButton("From Google Drive…", enabled = importLocked == null, onClick = {
                    scope.launch {
                        driveList = runCatching {
                            val token = app.driveSignIn.accessToken() ?: throw ImportException("Google Drive isn't signed in.")
                            withContext(Dispatchers.IO) { DriveFiles(DriveNetwork).listSettingsBackups(token, kind.written) }
                        }.getOrElse { onMessage(it.message ?: "Couldn't reach Google Drive."); null }
                        if (driveList?.isEmpty() == true) {
                            onMessage("No ${kind.title.lowercase()} backups on Google Drive yet.")
                            driveList = null
                        }
                    }
                })
            }
        }
    }

    lockedFile?.let { file ->
        AbDialog("Backup password", onDismiss = { lockedFile = null; password = "" }, confirmText = "Open", onConfirm = {
            runCatching { open(BackupFile.open(file, password)) }.onFailure { onMessage(it.message ?: "Couldn't open it.") }
            lockedFile = null
            password = ""
        }) {
            OutlinedTextField(password, { password = it }, label = { Text("Password") }, visualTransformation = PasswordVisualTransformation(), singleLine = true)
        }
    }

    driveList?.let { list ->
        val format = DateTimeFormatter.ofPattern("MMM d, h:mm a", Locale.US)
        AbDialog("From Google Drive", onDismiss = { driveList = null }) {
            list.take(30).forEach { backup ->
                Text(
                    "${backup.deviceName} · ${backup.modifiedAt.atZone(ZoneId.systemDefault()).format(format)}",
                    modifier = Modifier.fillMaxWidth().clickable {
                        driveList = null
                        scope.launch {
                            val json = runCatching {
                                val token = app.driveSignIn.accessToken() ?: throw ImportException("Google Drive isn't signed in.")
                                val bytes = withContext(Dispatchers.IO) { DriveFiles(DriveNetwork).download(token, backup.id) }
                                app.sync.openSettingsBackup(bytes) ?: throw ImportException("This backup was sealed with another key (from before a device was removed, or another group).")
                            }.getOrElse { onMessage(it.message ?: "Couldn't open it."); null }
                            json?.let { runCatching { open(Json.parseToJsonElement(it).jsonObject) }.onFailure { onMessage("The backup is damaged.") } }
                        }
                    }.padding(vertical = 8.dp),
                )
            }
        }
    }

    opened?.let { content ->
        AbDialog(
            "Restore ${kind.title.lowercase()}?", onDismiss = { opened = null }, confirmText = "Replace",
            onConfirm = {
                opened = null
                onMessage(PhoneKindBackup.import(app, content, kind, restore = true))
            },
        ) {
            Note(
                "Replace: what's in the backup comes back over later changes (task-list items made since stay). " +
                    "Merge: the newest edit of each item is kept. Settings with a wait (sleep mode off, ProductaBuddy) take it as usual.",
            )
            AbTonalButton("Merge instead", onClick = {
                opened = null
                onMessage(PhoneKindBackup.import(app, content, kind, restore = false))
            })
        }
    }
}
