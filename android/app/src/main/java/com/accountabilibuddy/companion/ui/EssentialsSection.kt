package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Visibility
import androidx.compose.material.icons.outlined.VisibilityOff
import androidx.compose.material3.Checkbox
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.AppFolder
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
import com.accountabilibuddy.companion.essentials.ProfileSync
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import java.util.UUID

/**
 * Settings → Essentials (the essentials screen and the always-allowed apps): ticked apps first (in the order they had
 * when this opened, so rows don't jump), an eye to hide one from the screen (still allowed), "Parts" to allow only some
 * screens of an app (e.g. Gemini's overlay), a folder per app; the folders, shown or search-only; when the screen comes
 * up; and the LockMyPhone-Extended hardcore profile.
 */
@Composable
internal fun EssentialsSection() {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val store = app.essentials
    val essentials by store.essentials.collectAsState()
    var all by remember { mutableStateOf(emptyList<com.accountabilibuddy.companion.essentials.LaunchableApp>()) }
    androidx.compose.runtime.LaunchedEffect(Unit) { all = com.accountabilibuddy.companion.essentials.AppCatalog.apps(context, refresh = true) }
    val firstOrder = remember { store.essentials.value.apps.map { it.packageName }.toSet() }
    var search by remember { mutableStateOf("") }
    var onUnlock by remember { mutableStateOf(store.onUnlockInSession) }
    var icons by remember { mutableStateOf(store.showIcons) }
    var hardcore by remember { mutableStateOf(store.hardcoreProfile.orEmpty()) }
    var useLmp by remember { mutableStateOf(store.useLockMyPhone) }
    var newFolder by remember { mutableStateOf("") }
    var partsOpen by remember { mutableStateOf<String?>(null) }
    val reader = remember { PhoneUsageReader(context) }

    Section("Essentials", summary = "${essentials.apps.size} apps · ${essentials.folders.size} folders${if (store.useLockMyPhone && store.hardcoreProfile != null) " · hardcore on" else ""}") {
        Text("Essentials are always allowed (no work card, not a pickup) and listed on the essentials screen, which comes up on unlocking during a session on another device or emergency time, and from the work card. Everything else is behind a search that shows nothing until Enter.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        ToggleRow("On unlocking during a session", "On another device, unlocking the phone lands on the essentials screen", onUnlock) {
            store.onUnlockInSession = it; onUnlock = it
        }
        ToggleRow("App icons", "Icons next to the names on the essentials screen", icons) { store.showIcons = it; icons = it }
        // The screen as it is in a session, for one minute (it closes by itself).
        androidx.compose.material3.OutlinedButton(
            onClick = { context.startActivity(com.accountabilibuddy.companion.essentials.EssentialsActivity.testIntent(context)) },
            modifier = Modifier.fillMaxWidth(),
        ) { Text("Try the essentials screen for 1 minute") }
        // The real screen with ↑ ↓ on every row: the order of apps and folders, apps inside folders, the Phone corner.
        androidx.compose.material3.Button(
            onClick = { context.startActivity(com.accountabilibuddy.companion.essentials.EssentialsActivity.arrangeIntent(context)) },
            modifier = Modifier.fillMaxWidth(),
        ) { Text("Arrange the essentials screen") }

        // Hardcore: LockMyPhone-Extended really locks the phone for the session.
        Text("Hardcore (LockMyPhone-Extended)", style = MaterialTheme.typography.labelLarge)
        ToggleRow(
            "Use LockMyPhone-Extended",
            "Off: AccountabiliBuddy never starts its profiles or keeps its \"${ProfileSync.PROFILE_NAME}\" profile in step",
            useLmp,
        ) { store.useLockMyPhone = it; useLmp = it }
        if (useLmp) OutlinedTextField(
            value = hardcore, onValueChange = { hardcore = it; store.hardcoreProfile = it }, singleLine = true,
            label = { Text("Its profile to start for each session (empty = off)") }, modifier = Modifier.fillMaxWidth(),
        )
        Text(
            if (EssentialsLaunch.lockMyPhoneInstalled(context)) "When a session starts on another device, LockMyPhone-Extended starts that profile for the session's length; it decides what's allowed. Its \"${ProfileSync.PROFILE_NAME}\" profile always allows exactly these essentials."
            else "LockMyPhone-Extended isn't installed on this phone.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        if (useLmp && EssentialsLaunch.lockMyPhoneInstalled(context) && hardcore != ProfileSync.PROFILE_NAME) {
            androidx.compose.material3.TextButton(onClick = { hardcore = ProfileSync.PROFILE_NAME; store.hardcoreProfile = ProfileSync.PROFILE_NAME }) {
                Text("Use \"${ProfileSync.PROFILE_NAME}\" (kept in sync with these essentials)")
            }
        }

        // Folders.
        Text("Folders", style = MaterialTheme.typography.labelLarge)
        essentials.folders.forEach { folder ->
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                Text("${folder.name} (${folder.packages.size})", style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
                FilterChip(selected = folder.shown, onClick = {
                    store.update { e -> e.copy(folders = e.folders.map { if (it.id == folder.id) it.copy(shown = !it.shown) else it }) }
                }, label = { Text(if (folder.shown) "Shown" else "Search only") })
                IconButton(onClick = { store.update { e -> e.copy(folders = e.folders.filterNot { it.id == folder.id }) } }) {
                    Icon(Icons.Outlined.Close, contentDescription = "Delete folder")
                }
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            OutlinedTextField(value = newFolder, onValueChange = { newFolder = it }, singleLine = true, label = { Text("New folder") }, modifier = Modifier.weight(1f))
            TextButton(enabled = newFolder.isNotBlank(), onClick = {
                val name = newFolder.trim()
                store.update { e -> e.copy(folders = e.folders + AppFolder(UUID.randomUUID().toString(), name, emptyList())) }
                newFolder = ""
            }) { Text("Add") }
        }

        // Apps: your essentials, then (only while searching) other apps to add. Listing every app at once was slow.
        Text("Apps", style = MaterialTheme.typography.labelLarge)
        OutlinedTextField(value = search, onValueChange = { search = it }, singleLine = true, label = { Text("Find an app to add") }, modifier = Modifier.fillMaxWidth())
        val selected = essentials.apps.map { it.packageName }.toSet() + firstOrder
        val first = all.filter { it.packageName in selected && (search.isBlank() || it.name.contains(search, ignoreCase = true)) }
        val rest = if (search.isBlank()) emptyList()
        else all.filter { it.packageName !in selected && (it.name.contains(search, ignoreCase = true) || it.packageName.contains(search, ignoreCase = true)) }.take(30)
        Text(
            if (all.isEmpty()) "Loading apps…" else "${essentials.apps.size} selected" + if (search.isBlank()) " · type above to add more" else "",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        (first + (if (rest.isEmpty()) emptyList() else listOf(null)) + rest).forEach { item ->
            if (item == null) {
                HorizontalDivider()
                Text("Other apps", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                return@forEach
            }

            val entry = essentials.apps.firstOrNull { it.packageName == item.packageName }
            val folder = essentials.folders.firstOrNull { item.packageName in it.packages }
            Column {
                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                    Checkbox(checked = entry != null, onCheckedChange = { store.setAllowed(item.packageName, it) })
                    Column(Modifier.weight(1f)) {
                        Text(item.name, style = MaterialTheme.typography.bodyMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        val notes = listOfNotNull(
                            folder?.name?.let { "in $it" },
                            entry?.parts?.let { "only ${it.size} part${if (it.size == 1) "" else "s"}" },
                            if (entry?.hidden == true) "hidden" else null,
                        )
                        if (notes.isNotEmpty()) Text(notes.joinToString(" · "), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    // Putting an app in a folder makes it an essential.
                    FolderMenu(essentials.folders, folder) { chosen -> store.setFolder(item.packageName, chosen?.id) }
                    if (entry != null) {
                        IconButton(onClick = { store.setHidden(item.packageName, !entry.hidden) }) {
                            Icon(if (entry.hidden) Icons.Outlined.VisibilityOff else Icons.Outlined.Visibility,
                                contentDescription = if (entry.hidden) "Show on the essentials screen" else "Hide from the essentials screen")
                        }
                        TextButton(onClick = { partsOpen = if (partsOpen == item.packageName) null else item.packageName }) { Text("Parts") }
                    }
                }
                if (entry != null && partsOpen == item.packageName) {
                    val screens = remember(item.packageName) { reader.screensOf(item.packageName) }
                    Column(Modifier.padding(start = 40.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            RadioButton(selected = entry.parts == null, onClick = { store.setParts(item.packageName, null) })
                            Text("The whole app", style = MaterialTheme.typography.bodyMedium)
                        }
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            RadioButton(selected = entry.parts != null, onClick = { store.setParts(item.packageName, entry.parts ?: emptySet()) })
                            Text("Only these parts", style = MaterialTheme.typography.bodyMedium)
                        }
                        if (entry.parts != null) {
                            if (screens.isEmpty()) {
                                Text("No parts seen yet: open the part you need once (e.g. the overlay), then come back. Needs Usage access.",
                                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            screens.forEach { screen ->
                                Row(verticalAlignment = Alignment.CenterVertically) {
                                    Checkbox(checked = screen in entry.parts, onCheckedChange = { on ->
                                        store.setParts(item.packageName, if (on) entry.parts + screen else entry.parts - screen)
                                    })
                                    Text(screenLabel(screen), style = MaterialTheme.typography.bodySmall)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun FolderMenu(folders: List<AppFolder>, current: AppFolder?, onChoose: (AppFolder?) -> Unit) {
    if (folders.isEmpty()) return
    var open by remember { mutableStateOf(false) }
    Column {
        TextButton(onClick = { open = true }) { Text(current?.name ?: "Folder") }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            DropdownMenuItem(text = { Text("No folder") }, onClick = { onChoose(null); open = false })
            folders.forEach { folder ->
                DropdownMenuItem(text = { Text(folder.name) }, onClick = { onChoose(folder); open = false })
            }
        }
    }
}

// "com.google…assistant.surfaces.voice.robin.ui.floaty.FloatyActivity" → "FloatyActivity (…floaty)".
private fun screenLabel(className: String): String {
    val parts = className.split('.')
    val name = parts.lastOrNull().orEmpty()
    val parent = parts.dropLast(1).lastOrNull()
    return if (parent != null) "$name (…$parent)" else name
}
