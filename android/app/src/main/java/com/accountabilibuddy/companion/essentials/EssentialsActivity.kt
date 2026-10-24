package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.drawable.Drawable
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ArrowDownward
import androidx.compose.material.icons.outlined.ArrowUpward
import androidx.compose.material.icons.outlined.Call
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material.icons.outlined.Folder
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.graphics.drawable.toBitmap
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.CompanionTheme
import java.time.Duration
import java.time.Instant
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * The essentials screen (in LockMyPhone-Extended's style; soft: nothing is blocked): during a session on another device,
 * work time or emergency phone time, unlocking lands here. Black, a big clock and the time left, the shown folders
 * (open on a tap, shut again when an app opens or the phone locks) and the essentials in the order arranged in
 * Settings, the Phone shortcut in a bottom corner (or in the list), and a search that shows nothing until Enter and then
 * only near-exact names ([AppMatch]). With [EXTRA_ARRANGE] it's the same screen with ↑ ↓ on every row, folder and app
 * inside a folder, and the Phone corner to choose. Opening anything that isn't an essential still brings the work
 * card back and counts as a pickup.
 */
class EssentialsActivity : ComponentActivity() {
    // Bumped when the screen goes away (an app opened, the phone locked): folders shut, the search clears.
    private var resetKey by mutableIntStateOf(0)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val app = application as CompanionApp
        val arranging = intent.getBooleanExtra(EXTRA_ARRANGE, false)
        setContent {
            CompanionTheme {
                androidx.compose.runtime.key(resetKey) {
                    EssentialsScreen(app, arranging, onOpen = ::openApp, onDial = ::dial, onDone = ::finish)
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        (application as CompanionApp).essentialsOnScreen = true
    }

    override fun onStop() {
        super.onStop()
        (application as CompanionApp).essentialsOnScreen = false
        resetKey++
    }

    private fun openApp(packageName: String) {
        packageManager.getLaunchIntentForPackage(packageName)?.let { intent ->
            runCatching { startActivity(intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
        }
    }

    private fun dial() {
        runCatching { startActivity(Intent(Intent.ACTION_DIAL).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
    }

    companion object {
        const val EXTRA_ARRANGE = "arrange"

        /** Opens the essentials screen (from the work card, emergency time, or an unlock during a session). */
        fun intent(context: Context): Intent =
            Intent(context, EssentialsActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)

        /** The same screen to arrange (Settings → Essentials). */
        fun arrangeIntent(context: Context): Intent = intent(context).putExtra(EXTRA_ARRANGE, true)
    }
}

/** A launchable app: package, name, icon (null until loaded or with icons off). */
data class LaunchableApp(val packageName: String, val name: String, val icon: Drawable?)

/** Every app with a launcher entry, named, A–Z (this app left out). */
fun launchableApps(context: Context, withIcons: Boolean): List<LaunchableApp> {
    val pm = context.packageManager
    return pm.queryIntentActivities(Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER), PackageManager.MATCH_ALL)
        .filter { it.activityInfo.packageName != context.packageName }
        .distinctBy { it.activityInfo.packageName }
        .map { LaunchableApp(it.activityInfo.packageName, it.loadLabel(pm).toString(), if (withIcons) it.loadIcon(pm) else null) }
        .sortedBy { it.name.lowercase() }
}

@Composable
private fun EssentialsScreen(app: CompanionApp, arranging: Boolean, onOpen: (String) -> Unit, onDial: () -> Unit, onDone: () -> Unit) {
    val context = androidx.compose.ui.platform.LocalContext.current
    val store = app.essentials
    val essentials by store.essentials.collectAsState()
    var corner by remember { mutableStateOf(store.phoneCorner) }
    val all = remember { launchableApps(context, store.showIcons) }
    val byPackage = remember(all) { all.associateBy { it.packageName } }
    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(Unit) {
        while (true) {
            now = Instant.now()
            kotlinx.coroutines.delay(1_000)
        }
    }
    var query by remember { mutableStateOf("") }
    var searched by remember { mutableStateOf<String?>(null) }
    val open = remember { mutableStateOf(setOf<String>()) }
    val keys = essentials.homeKeys { byPackage[it]?.name ?: it }
    val results = searched?.let { text ->
        Pair(essentials.folders.filter { AppMatch.matches(text, it.name) }, all.filter { AppMatch.matches(text, it.name) })
    }
    val moves = if (arranging) Moves(store, keys) else null
    // Hidden apps never show on the screen, not even inside a folder: only the search finds them.
    val hidden = essentials.apps.filter { it.hidden }.map { it.packageName }.toSet()

    Box(Modifier.fillMaxSize().background(Color.Black).statusBarsPadding().navigationBarsPadding().imePadding()) {
        Column(Modifier.fillMaxSize().padding(horizontal = 24.dp)) {
            if (arranging) {
                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth().padding(top = 8.dp)) {
                    Text("Arrange · ↑ ↓ to move", color = Color(0xFF7DD3C0), fontSize = 16.sp, modifier = Modifier.weight(1f))
                    TextButton(onClick = onDone) { Text("Done", color = Color.White) }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text("Phone:", color = Color(0xCCFFFFFF))
                    listOf("left" to "Bottom left", "right" to "Bottom right", "list" to "In the list").forEach { (value, label) ->
                        FilterChip(selected = corner == value, onClick = { store.phoneCorner = value; corner = value }, label = { Text(label) })
                    }
                }
            }
            Row(verticalAlignment = Alignment.Top, modifier = Modifier.fillMaxWidth().padding(top = 16.dp)) {
                Text(LocalTime.now().format(DateTimeFormatter.ofPattern("h:mm", Locale.US)), color = Color.White, fontSize = 56.sp, modifier = Modifier.weight(1f))
                timeLeft(app, now)?.let { Text(it, color = Color(0xCCFFFFFF), fontSize = 16.sp, modifier = Modifier.padding(top = 18.dp)) }
            }
            LazyColumn(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                item { Spacer(Modifier.height(40.dp)) }
                if (corner == "list") {
                    item { PhoneRow(onDial) }
                }
                if (results != null && !arranging) {
                    if (results.first.isEmpty() && results.second.isEmpty()) {
                        item { Text("Nothing called \"$searched\". Type its name.", color = Color(0x99FFFFFF), modifier = Modifier.padding(vertical = 12.dp)) }
                    }
                    results.first.forEach { folder -> folderItems(folder, byPackage, open, onOpen, null, hidden) }
                    results.second.forEach { found -> item(key = "r-" + found.packageName) { AppRow(found, onOpen, null) } }
                } else {
                    keys.forEach { key ->
                        when {
                            key.startsWith("folder:") -> essentials.folders.firstOrNull { Essentials.folderKey(it.id) == key }
                                ?.let { folder -> folderItems(folder, byPackage, open, onOpen, moves, hidden) }
                            else -> byPackage[key.removePrefix("app:")]?.let { found ->
                                item(key = key) { AppRow(found, onOpen, moves?.let { m -> { by: Int -> m.row(key, by) } }) }
                            }
                        }
                    }
                }
                item { Spacer(Modifier.height(72.dp)) }
            }
            if (!arranging) {
                OutlinedTextField(
                    value = query,
                    onValueChange = { query = it; searched = null },
                    singleLine = true,
                    placeholder = { Text("Type an app's name, then Enter", color = Color(0x66FFFFFF)) },
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                    keyboardActions = KeyboardActions(onSearch = { searched = query.trim().takeIf { it.isNotEmpty() } }),
                    colors = OutlinedTextFieldDefaults.colors(focusedTextColor = Color.White, unfocusedTextColor = Color.White),
                    modifier = Modifier.fillMaxWidth().padding(vertical = 12.dp),
                )
            }
        }
        // The Phone shortcut in a bottom corner, above the search box.
        if (corner != "list") {
            Surface(
                shape = CircleShape, color = Color(0xFF1F2937),
                modifier = Modifier.align(if (corner == "left") Alignment.BottomStart else Alignment.BottomEnd)
                    .padding(start = 24.dp, end = 24.dp, bottom = if (arranging) 24.dp else 92.dp).size(60.dp).clickable(onClick = onDial),
            ) {
                Box(contentAlignment = Alignment.Center) { Icon(Icons.Outlined.Call, contentDescription = "Phone", tint = Color.White, modifier = Modifier.size(30.dp)) }
            }
        }
    }
}

/** ↑ ↓ for the screen's rows and the apps inside folders (arrange mode). */
private class Moves(val store: EssentialsStore, val keys: List<String>) {
    fun row(key: String, by: Int) = store.moveHome(keys, key, by)
    fun inFolder(folderId: String, packageName: String, by: Int) = store.moveInFolder(folderId, packageName, by)
}

@Composable
private fun PhoneRow(onDial: () -> Unit) {
    Row(Modifier.fillMaxWidth().clickable(onClick = onDial).padding(vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        Icon(Icons.Outlined.Call, contentDescription = null, tint = Color.White, modifier = Modifier.size(32.dp))
        Spacer(Modifier.size(14.dp))
        Text("Phone", color = Color.White, fontSize = 20.sp)
    }
}

private fun LazyListScope.folderItems(
    folder: AppFolder, byPackage: Map<String, LaunchableApp>, open: MutableState<Set<String>>, onOpen: (String) -> Unit, moves: Moves?,
    hidden: Set<String>,
) {
    val expanded = folder.id in open.value
    item(key = "f-" + folder.id) {
        Row(
            Modifier.fillMaxWidth().clickable { open.value = if (expanded) open.value - folder.id else open.value + folder.id }.padding(vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(Icons.Outlined.Folder, contentDescription = null, tint = Color.White, modifier = Modifier.size(32.dp))
            Spacer(Modifier.size(14.dp))
            Text(folder.name, color = Color.White, fontSize = 20.sp, modifier = Modifier.weight(1f))
            moves?.let { MoveButtons { by -> it.row(Essentials.folderKey(folder.id), by) } }
            Icon(if (expanded) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore, contentDescription = null, tint = Color(0x99FFFFFF), modifier = Modifier.size(28.dp))
        }
    }
    if (expanded) {
        // Inside a folder: its own order (arranged here), else A–Z for apps added without arranging.
        folder.packages.filter { it !in hidden }.mapNotNull { byPackage[it] }.forEach { inside ->
            item(key = "f-" + folder.id + "-" + inside.packageName) {
                Box(Modifier.padding(start = 28.dp)) {
                    AppRow(inside, onOpen, moves?.let { m -> { by: Int -> m.inFolder(folder.id, inside.packageName, by) } })
                }
            }
        }
    }
}

@Composable
private fun AppRow(app: LaunchableApp, onOpen: (String) -> Unit, onMove: ((Int) -> Unit)?) {
    Row(
        Modifier.fillMaxWidth().clickable(enabled = onMove == null) { onOpen(app.packageName) }.padding(vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        app.icon?.let { icon ->
            Image(icon.toBitmap(96, 96).asImageBitmap(), contentDescription = null, modifier = Modifier.size(32.dp))
            Spacer(Modifier.size(14.dp))
        }
        Text(app.name, color = Color.White, fontSize = 20.sp, modifier = Modifier.weight(1f))
        onMove?.let { MoveButtons(it) }
    }
}

@Composable
private fun MoveButtons(onMove: (Int) -> Unit) {
    IconButton(onClick = { onMove(-1) }) { Icon(Icons.Outlined.ArrowUpward, contentDescription = "Move up", tint = Color.White) }
    IconButton(onClick = { onMove(1) }) { Icon(Icons.Outlined.ArrowDownward, contentDescription = "Move down", tint = Color.White) }
}

// The time left: the emergency time, else the session on another device (from its sync file), else nothing.
private fun timeLeft(app: CompanionApp, now: Instant): String? {
    app.emergency.active(now)?.let { return "Phone time · ${minutes(Duration.between(now, it.end))} left" }
    val running = app.sync.received.value
        .filter { it.file.device.kind == com.accountabilibuddy.shared.sync.SyncDeviceCard.KIND_PC }
        .mapNotNull { it.file.running }
        .filter { it.phaseEndsAt?.isAfter(now) ?: true }
        .maxByOrNull { it.phaseStartedAt }
        ?: return null
    return running.phaseEndsAt?.let { "${if (running.isBreak) "Break" else "Focus"} · ${minutes(Duration.between(now, it))} left" } ?: "Session running"
}

private fun minutes(left: Duration): String {
    val m = left.toMinutes().coerceAtLeast(0)
    return if (m >= 60) "${m / 60}h ${"%02d".format(m % 60)}m" else "${m}m"
}
