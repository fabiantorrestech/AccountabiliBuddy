package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.relocation.BringIntoViewRequester
import androidx.compose.foundation.relocation.bringIntoViewRequester
import androidx.compose.foundation.selection.toggleable
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Refresh
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.core.graphics.drawable.toBitmap
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.essentials.AppCatalog
import com.accountabilibuddy.companion.essentials.AppFolder
import com.accountabilibuddy.companion.essentials.EssentialApp
import com.accountabilibuddy.companion.essentials.EssentialsActivity
import com.accountabilibuddy.companion.essentials.EssentialsKind
import com.accountabilibuddy.companion.essentials.EssentialsLaunch
import com.accountabilibuddy.companion.essentials.EssentialsStore
import com.accountabilibuddy.companion.essentials.LaunchableApp
import com.accountabilibuddy.companion.essentials.LockMyPhoneProfiles
import com.accountabilibuddy.companion.essentials.LunchLockLift
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbCheckRow
import com.accountabilibuddy.companion.ui.kit.AbSearchField
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbSwitch
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlinx.coroutines.launch
import java.util.UUID

/**
 * Settings → Guards → Phone guard → Essentials (the essentials screen and the always-allowed apps; board 17b): the allowed apps
 * first (in the order they had when this opened, so rows don't jump; a tap on one opens its folder, its eye that hides it from the
 * screen while it stays allowed, and "Parts" to allow only some screens of an app, e.g. Gemini's overlay), a search to add more,
 * the folders, shown or search-only; when the screen comes up; and the LockMyPhone-Extended hardcore profile. With [kind] Bedtime it's
 * Settings → Sleep → Bedtime essentials: the night's own list, screen and LockMyPhone-Extended profile (from bedtime until the wake
 * time), in one folding card. [scrollToLock] opens the page scrolled to the LockMyPhone-Extended card.
 */
@Composable
internal fun EssentialsSection(kind: EssentialsKind = EssentialsKind.Focus, startOpen: Boolean = false, scrollToLock: Boolean = false) =
    GuardsScope { EssentialsSectionBody(kind, startOpen, scrollToLock) }

@Composable
private fun EssentialsSectionBody(kind: EssentialsKind, startOpen: Boolean, scrollToLock: Boolean) {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val store = app.essentialsOf(kind)
    val night = kind == EssentialsKind.Bedtime
    val syncedName = kind.lockMyPhoneName
    // The night's synced profile needs a LockMyPhone-Extended that keeps one per slot.
    val canSync = !night || EssentialsLaunch.lockMyPhoneHasSlots(context)
    val essentials by store.essentials.collectAsState()
    var all by remember { mutableStateOf(emptyList<LaunchableApp>()) }
    LaunchedEffect(Unit) { all = AppCatalog.apps(context, refresh = true) }
    val firstOrder = remember { store.essentials.value.apps.map { it.packageName }.toSet() }
    var search by remember { mutableStateOf("") }
    var onUnlock by remember { mutableStateOf(store.onUnlockInSession) }
    var icons by remember { mutableStateOf(store.showIcons) }
    var hardcore by remember { mutableStateOf(store.hardcoreProfile.orEmpty()) }
    var useLmp by remember { mutableStateOf(store.useLockMyPhone) }
    var newFolder by remember { mutableStateOf("") }
    var opened by remember { mutableStateOf<String?>(null) }
    val reader = remember { PhoneUsageReader(context) }
    val lockCard = remember { BringIntoViewRequester() }
    LaunchedEffect(scrollToLock) { if (scrollToLock) lockCard.bringIntoView() }

    val summary = if (night) {
        listOfNotNull(
            "${essentials.apps.size} apps",
            if (onUnlock) "on unlock at night" else "screen off",
            "lock for the night".takeIf { useLmp && hardcore.isNotBlank() },
        ).joinToString(" · ")
    } else {
        "${essentials.apps.size} apps · ${essentials.folders.size} folders${if (store.useLockMyPhone && store.hardcoreProfile != null) " · hardcore on" else ""}"
    }

    val cards: @Composable (Boolean) -> Unit = { framed ->
        GuardsRestNote()
        Note(
            if (night) "The apps for the night, from bedtime until your wake time: listed on a black screen that comes up when you unlock the phone at night. Everything else is behind a search that shows nothing until Enter. Nothing is blocked unless LockMyPhone-Extended locks the phone for the night (below)."
            else "What you can open from \"Phone down\" while a PC session runs. Essentials are always allowed (no work card, not a pickup) and listed on the essentials screen, which comes up on unlocking during a session on another device or emergency time, and from the work card. Everything else is behind a search that shows nothing until Enter.",
        )
        if (night && !app.store.state.collectAsState().value.sleep.enabled) {
            Note("Sleep mode is off: the night needs a bedtime and a wake time (Sleep times).", color = Ab.colors.drift)
        }
        if (!night && !reader.hasAccess()) {
            Note("Grant Usage access so Essentials knows when you leave an essential app.", color = Ab.colors.waiting)
        }

        // Apps: your essentials, then (only while searching) other apps to add. Listing every app at once was slow.
        EssentialsGroup(if (night) "Apps" else "Always allowed", framed) {
            AbSearchField(search, { search = it }, placeholder = "Find an app to add")
            val selected = essentials.apps.map { it.packageName }.toSet() + firstOrder
            val first = all.filter { it.packageName in selected && (search.isBlank() || it.name.contains(search, ignoreCase = true)) }
            val rest = if (search.isBlank()) emptyList()
            else all.filter { it.packageName !in selected && (it.name.contains(search, ignoreCase = true) || it.packageName.contains(search, ignoreCase = true)) }.take(30)
            Note(if (all.isEmpty()) "Loading apps…" else "${essentials.apps.size} selected" + if (search.isBlank()) " · type above to add more" else "")
            first.forEach { item ->
                AppRow(
                    item, essentials.apps.firstOrNull { it.packageName == item.packageName }, essentials.folders.firstOrNull { item.packageName in it.packages },
                    essentials.folders, store, reader, expanded = opened == item.packageName,
                    onToggleOpen = { opened = if (opened == item.packageName) null else item.packageName },
                )
            }
            if (rest.isNotEmpty()) {
                AbSectionLabel("Other apps", Modifier.padding(top = 8.dp))
                rest.forEach { item -> AppRow(item, null, null, essentials.folders, store, reader, expanded = false, onToggleOpen = {}) }
            }
        }

        // The screen: when it comes up, whether it shows icons, and a way to try and arrange it.
        EssentialsGroup(if (night) "The bedtime essentials screen" else "The essentials screen", framed) {
            ToggleRow(
                if (night) "On unlocking at night" else "On unlocking during a session",
                if (night) "From bedtime until your wake time, unlocking the phone lands on the bedtime essentials screen (a session on another device shows its own)"
                else "On another device, unlocking the phone lands on the essentials screen",
                onUnlock,
            ) {
                store.onUnlockInSession = it; onUnlock = it
            }
            ToggleRow("App icons", "Icons next to the names on the essentials screen", icons) { store.showIcons = it; icons = it }
            if (night) {
                GuardTextButton("Replace with a copy of my focus essentials", onClick = { store.update { app.essentials.essentials.value } })
            }
            // What a session will look like, for one minute: LockMyPhone-Extended's real lock with the chosen profile when
            // it's used (that's the lock a session gets), else this app's essentials screen (it closes by itself).
            val lmpTest = useLmp && hardcore.isNotBlank() && EssentialsLaunch.lockMyPhoneInstalled(context)
            GuardTonalButton(
                if (lmpTest) "Try LockMyPhone-Extended's \"$hardcore\" lock for 1 minute" else "Try the ${if (night) "bedtime " else ""}essentials screen for 1 minute",
                onClick = {
                    if (lmpTest) EssentialsLaunch.startTestLock(context, hardcore)
                    else context.startActivity(EssentialsActivity.testIntent(context, kind))
                },
                modifier = Modifier.fillMaxWidth(),
            )
            if (lmpTest) {
                Note("Asks LockMyPhone-Extended for a 1-minute lock with that profile. It won't start while a stronger lock (schedule, NFC, place…) runs, and a Limited profile gives free use first.")
            }
            // The real screen with ↑ ↓ on every row: the order of apps and folders, apps inside folders, the Phone corner.
            GuardTonalButton(
                "Arrange the ${if (night) "bedtime " else ""}essentials screen",
                onClick = { context.startActivity(EssentialsActivity.arrangeIntent(context, kind)) },
                modifier = Modifier.fillMaxWidth(),
            )
        }

        // Folders.
        EssentialsGroup("Folders", framed) {
            essentials.folders.forEach { folder ->
                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                    Text("${folder.name} (${folder.packages.size})", style = Ab.type.bodyMedium, color = Ab.colors.ink, modifier = Modifier.weight(1f))
                    GuardChip(if (folder.shown) "Shown" else "Search only", selected = folder.shown, onClick = {
                        store.update { e -> e.copy(folders = e.folders.map { if (it.id == folder.id) it.copy(shown = !it.shown) else it }) }
                    })
                    GuardIconAction(Icons.Outlined.Close, "Delete folder") { store.update { e -> e.copy(folders = e.folders.filterNot { it.id == folder.id }) } }
                }
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                AbTextField(newFolder, { newFolder = it }, label = "New folder", modifier = Modifier.weight(1f))
                // Folder names are unique (LockMyPhone-Extended's synced profile names folders by name).
                GuardTextButton("Add", enabled = newFolder.isNotBlank() && essentials.folders.none { it.name.equals(newFolder.trim(), ignoreCase = true) }, onClick = {
                    val name = newFolder.trim()
                    store.update { e -> e.copy(folders = e.folders + AppFolder(UUID.randomUUID().toString(), name, emptyList())) }
                    newFolder = ""
                })
            }
        }

        // Hardcore: LockMyPhone-Extended really locks the phone for the session (or the night).
        EssentialsGroup(if (night) "Lock for the night (LockMyPhone-Extended)" else "Hardcore (LockMyPhone-Extended)", framed, Modifier.bringIntoViewRequester(lockCard)) {
            if (night) {
                if (!useLmp) {
                    Note("LockMyPhone-Extended is off (Settings › Guards › Phone guard › Essentials › Use LockMyPhone-Extended).")
                }
            } else {
                ToggleRow(
                    "Use LockMyPhone-Extended",
                    "Off: AccountabiliBuddy never starts its profiles or keeps its \"$syncedName\" profiles in step",
                    useLmp,
                ) {
                    store.useLockMyPhone = it; useLmp = it
                    // The night's synced profile follows the same switch.
                    app.bedtimeEssentials.resync()
                }
            }
            LockMyPhoneProfilePicker(
                selected = hardcore.ifBlank { null },
                enabled = useLmp,
                noneLabel = if (night) "none (no lock at night)" else "none (no lock for sessions)",
                onPick = { name -> hardcore = name.orEmpty(); store.hardcoreProfile = name },
            )
            Note(
                when {
                    !EssentialsLaunch.lockMyPhoneInstalled(context) -> "LockMyPhone-Extended isn't installed on this phone."
                    night && !canSync -> "At bedtime LockMyPhone-Extended starts that profile until your wake time; it decides what's allowed. Update LockMyPhone-Extended to 0.13.0 to get an \"$syncedName\" profile kept in step with this list."
                    night -> "At bedtime LockMyPhone-Extended starts that profile until your wake time (or at your first unlock after it); it decides what's allowed. Its \"$syncedName\" profile always allows exactly these bedtime essentials."
                    else -> "When a session starts on another device, LockMyPhone-Extended starts that profile for the session's length; it decides what's allowed. Its \"$syncedName\" profile always allows exactly these essentials."
                },
            )
            if (useLmp && canSync && EssentialsLaunch.lockMyPhoneInstalled(context) && hardcore != syncedName) {
                GuardTextButton("Use \"$syncedName\" (kept in sync with these essentials)", onClick = { hardcore = syncedName; store.hardcoreProfile = syncedName })
            }
            if (!night) LunchLiftSetting(enabled = useLmp && hardcore.isNotBlank())
        }
    }

    if (night) {
        // One folding card in Sleep: the groups are plain blocks inside it.
        Section("Bedtime essentials", summary = summary, startOpen = startOpen) {
            Column(verticalArrangement = Arrangement.spacedBy(14.dp)) { cards(false) }
        }
    } else {
        Column(verticalArrangement = Arrangement.spacedBy(12.dp)) { cards(true) }
    }
}

/** One group of the essentials page: a card with its label, or (inside Sleep's folding card) a plain block with the label. */
@Composable
private fun EssentialsGroup(title: String, framed: Boolean, modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    if (framed) {
        AbCard(modifier, verticalSpacing = 8.dp) {
            AbSectionLabel(title)
            content()
        }
    } else {
        Column(modifier, verticalArrangement = Arrangement.spacedBy(8.dp)) {
            AbSectionLabel(title)
            content()
        }
    }
}

/**
 * One app: its icon and name with a switch (allowed or not). For an allowed app a tap opens what can be set for it: its folder,
 * whether it shows on the essentials screen (the eye) and "Parts" (only some screens of it).
 */
@Composable
private fun AppRow(
    item: LaunchableApp,
    entry: EssentialApp?,
    folder: AppFolder?,
    folders: List<AppFolder>,
    store: EssentialsStore,
    reader: PhoneUsageReader,
    expanded: Boolean,
    onToggleOpen: () -> Unit,
) {
    val allowed = entry != null
    val locked = LocalGuardsLocked.current
    Column {
        Row(
            Modifier.fillMaxWidth().heightIn(min = 56.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            // The name opens the app's extra settings (only for an allowed app); the switch allows or removes it.
            Row(
                Modifier.weight(1f).heightIn(min = 56.dp).clickable(enabled = allowed, role = Role.Button, onClick = onToggleOpen),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                AppIcon(item)
                Column(Modifier.weight(1f)) {
                    Text(item.name, style = Ab.type.body, fontWeight = FontWeight.Medium, color = Ab.colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    val notes = listOfNotNull(
                        folder?.name?.let { "in $it" },
                        entry?.parts?.let { "only ${it.size} part${if (it.size == 1) "" else "s"}" },
                        if (entry?.hidden == true) "hidden" else null,
                    )
                    if (notes.isNotEmpty()) Text(notes.joinToString(" · "), style = Ab.type.small, color = Ab.colors.muted)
                }
            }
            Box(
                Modifier.heightIn(min = 56.dp).toggleable(value = allowed, enabled = !locked, role = Role.Switch) { store.setAllowed(item.packageName, it) },
                contentAlignment = Alignment.Center,
            ) { AbSwitch(allowed, enabled = !locked) }
        }
        if (entry != null && expanded) AppDetails(item, entry, folder, folders, store, reader)
    }
}

// An allowed app's extra settings: its folder, whether it is hidden from the screen, and which parts of it are allowed.
@Composable
private fun AppDetails(item: LaunchableApp, entry: EssentialApp, folder: AppFolder?, folders: List<AppFolder>, store: EssentialsStore, reader: PhoneUsageReader) {
    var showParts by remember(item.packageName) { mutableStateOf(entry.parts != null) }
    Column(Modifier.padding(start = 48.dp, bottom = 8.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            // Putting an app in a folder makes it an essential.
            FolderMenu(folders, folder) { chosen -> store.setFolder(item.packageName, chosen?.id) }
            // Hidden: still allowed, but only found by search on the essentials screen.
            GuardChip(if (entry.hidden) "Hidden from the screen" else "Shown on the screen", selected = !entry.hidden, onClick = { store.setHidden(item.packageName, !entry.hidden) })
        }
        GuardTextButton(if (showParts) "Hide parts" else "Parts", onClick = { showParts = !showParts })
        if (showParts) {
            val screens = remember(item.packageName) { reader.screensOf(item.packageName) }
            GuardChoiceRow("The whole app", selected = entry.parts == null) { store.setParts(item.packageName, null) }
            GuardChoiceRow("Only these parts", selected = entry.parts != null) { store.setParts(item.packageName, entry.parts ?: emptySet()) }
            if (entry.parts != null) {
                if (screens.isEmpty()) {
                    Note("No parts seen yet: open the part you need once (e.g. the overlay), then come back. Needs Usage access.")
                }
                screens.forEach { screen ->
                    AbCheckRow(screenLabel(screen), checked = screen in entry.parts, enabled = !LocalGuardsLocked.current, onCheckedChange = { on ->
                        store.setParts(item.packageName, if (on) entry.parts + screen else entry.parts - screen)
                    })
                }
            }
        }
    }
}

/** An app's icon on the second tone, 36 dp (the package's own icon; nothing is downloaded). Blank when the app has none. */
@Composable
private fun AppIcon(item: LaunchableApp) {
    val bitmap: ImageBitmap? = remember(item.packageName) { item.icon?.let { runCatching { it.toBitmap(96, 96).asImageBitmap() }.getOrNull() } }
    Box(Modifier.size(36.dp), contentAlignment = Alignment.Center) {
        if (bitmap != null) Image(bitmap, contentDescription = null, modifier = Modifier.size(32.dp))
    }
}

/** A 44 dp icon button that takes no tap while the guards rest. */
@Composable
private fun GuardIconAction(icon: ImageVector, description: String, onClick: () -> Unit) {
    val locked = LocalGuardsLocked.current
    Box(Modifier.size(44.dp).clickable(enabled = !locked, role = Role.Button, onClick = onClick), contentAlignment = Alignment.Center) {
        Icon(icon, contentDescription = description, tint = if (locked) Ab.colors.faint else Ab.colors.muted, modifier = Modifier.size(22.dp))
    }
}

@Composable
private fun FolderMenu(folders: List<AppFolder>, current: AppFolder?, onChoose: (AppFolder?) -> Unit) {
    if (folders.isEmpty()) return
    var open by remember { mutableStateOf(false) }
    Column {
        GuardValueButton(current?.name ?: "Folder", onClick = { open = true })
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

/**
 * The LockMyPhone-Extended profile to start for each session: a dropdown of its profiles (read from it), greyed out
 * while LockMyPhone-Extended isn't used, with a refresh for profiles made since. Too old a LockMyPhone-Extended to
 * list them: the current name stays and it says to update.
 */
@Composable
private fun LockMyPhoneProfilePicker(selected: String?, enabled: Boolean, noneLabel: String, onPick: (String?) -> Unit) {
    val context = LocalContext.current
    var names by remember { mutableStateOf<List<String>?>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var open by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    fun reload() = scope.launch {
        loading = true
        names = LockMyPhoneProfiles.names(context)
        loading = false
    }
    LaunchedEffect(Unit) { reload() }

    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.weight(1f)) {
            GuardValueButton(selected?.let { "Profile: $it" } ?: "Profile: $noneLabel", onClick = { open = true }, enabled = enabled && names != null, modifier = Modifier.fillMaxWidth())
            DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                DropdownMenuItem(text = { Text(noneLabel.replaceFirstChar { it.uppercase() }) }, onClick = { onPick(null); open = false })
                names.orEmpty().forEach { name ->
                    DropdownMenuItem(text = { Text(name) }, onClick = { onPick(name); open = false })
                }
            }
        }
        if (enabled) GuardIconAction(Icons.Outlined.Refresh, "Look for new LockMyPhone-Extended profiles") { reload() }
    }
    val note = when {
        !enabled -> null
        loading -> "Reading LockMyPhone-Extended's profiles…"
        names == null -> "Update LockMyPhone-Extended to pick from its profiles (it needs to be installed before this app, or reinstall this app after it)."
        names!!.isEmpty() -> "LockMyPhone-Extended has no profiles yet: make one there, then refresh."
        selected != null && selected !in names!! -> "\"$selected\" isn't in LockMyPhone-Extended any more: pick another."
        else -> null
    }
    note?.let { Note(it) }
}

/** "Lift the lock for lunch": lunch stops the session's LockMyPhone-Extended lock (needs its Intent API token). */
@Composable
private fun LunchLiftSetting(enabled: Boolean) {
    val context = LocalContext.current
    var on by remember { mutableStateOf(LunchLockLift.enabled(context)) }
    var hasToken by remember { mutableStateOf(LunchLockLift.hasToken(context)) }
    var token by remember { mutableStateOf("") }
    var note by remember { mutableStateOf(LunchLockLift.note(context)) }
    ToggleRow(
        "Lift the lock for lunch",
        "When lunch starts (on any device) the session's LockMyPhone-Extended lock is stopped; it comes back when lunch ends. Only a lock started for the session, never one you started there or the night's.",
        on,
    ) { LunchLockLift.setEnabled(context, it); on = it }
    if (on) {
        AbTextField(
            token, { token = it }, label = if (hasToken) "Token saved (paste to replace)" else "Intent API token",
            visualTransformation = PasswordVisualTransformation(),
        )
        Note("Paste the token from LockMyPhone-Extended → Settings → Intent API; turn on \"Allow stopping a lock by intent\" there.")
        Row(verticalAlignment = Alignment.CenterVertically) {
            GuardTextButton("Save token", enabled = token.isNotBlank(), onClick = {
                LunchLockLift.saveToken(context, token); token = ""; hasToken = LunchLockLift.hasToken(context); note = null
            })
            GuardTextButton("Remove", enabled = hasToken, onClick = {
                LunchLockLift.saveToken(context, ""); hasToken = false; note = null
            })
            GuardTextButton("Test stop", enabled = hasToken, onClick = { note = LunchLockLift.testStop(context) })
        }
        if (!enabled) {
            Note("Choose a LockMyPhone-Extended profile for sessions above first.")
        }
        (note ?: LunchLockLift.note(context))?.let { Note(it, color = Ab.colors.drift) }
    }
}
