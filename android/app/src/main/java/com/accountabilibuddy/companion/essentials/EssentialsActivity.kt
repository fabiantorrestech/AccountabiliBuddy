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
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Call
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material.icons.outlined.Folder
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
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
 * work time or emergency phone time, unlocking lands here. Black, a big clock and the time left, Phone, the shown
 * folders (open on a tap, shut again when an app opens or the phone locks), the essentials A–Z, and a search that
 * shows nothing until Enter and then only near-exact names ([AppMatch]). Opening anything that isn't an essential
 * still brings the work card back and counts as a pickup.
 */
class EssentialsActivity : ComponentActivity() {
    // Bumped when the screen goes away (an app opened, the phone locked): folders shut, the search clears.
    private var resetKey by mutableIntStateOf(0)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val app = application as CompanionApp
        setContent {
            CompanionTheme {
                androidx.compose.runtime.key(resetKey) {
                    EssentialsScreen(app, onOpen = ::openApp, onDial = ::dial)
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
        /** Opens the essentials screen (from the work card, emergency time, or an unlock during a session). */
        fun intent(context: Context): Intent =
            Intent(context, EssentialsActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
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
private fun EssentialsScreen(app: CompanionApp, onOpen: (String) -> Unit, onDial: () -> Unit) {
    val context = androidx.compose.ui.platform.LocalContext.current
    val store = app.essentials
    val essentials = store.essentials.value
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

    val shownApps = essentials.apps.filter { !it.hidden }.mapNotNull { byPackage[it.packageName] }.sortedBy { it.name.lowercase() }
    val shownFolders = essentials.folders.filter { it.shown }
    val results = searched?.let { text ->
        Pair(
            essentials.folders.filter { AppMatch.matches(text, it.name) },
            all.filter { AppMatch.matches(text, it.name) },
        )
    }

    Box(Modifier.fillMaxSize().background(Color.Black).statusBarsPadding().navigationBarsPadding().imePadding()) {
        Column(Modifier.fillMaxSize().padding(horizontal = 24.dp)) {
            Row(verticalAlignment = Alignment.Top, modifier = Modifier.fillMaxWidth().padding(top = 16.dp)) {
                Text(LocalTime.now().format(DateTimeFormatter.ofPattern("h:mm", Locale.US)), color = Color.White, fontSize = 56.sp, modifier = Modifier.weight(1f))
                timeLeft(app, now)?.let { Text(it, color = Color(0xCCFFFFFF), fontSize = 16.sp, modifier = Modifier.padding(top = 18.dp)) }
            }
            LazyColumn(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                item { Spacer(Modifier.height(40.dp)) }
                item { Row(Modifier.fillMaxWidth().clickable(onClick = onDial).padding(vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Outlined.Call, contentDescription = null, tint = Color.White, modifier = Modifier.size(32.dp))
                    Spacer(Modifier.size(14.dp))
                    Text("Phone", color = Color.White, fontSize = 20.sp)
                } }
                if (results != null) {
                    if (results.first.isEmpty() && results.second.isEmpty()) {
                        item { Text("Nothing called \"$searched\". Type its name.", color = Color(0x99FFFFFF), modifier = Modifier.padding(vertical = 12.dp)) }
                    }
                    results.first.forEach { folder -> folderItems(folder, byPackage, open, onOpen) }
                    items(results.second, key = { "r-" + it.packageName }) { AppRow(it, onOpen) }
                } else {
                    shownFolders.forEach { folder -> folderItems(folder, byPackage, open, onOpen) }
                    items(shownApps, key = { it.packageName }) { AppRow(it, onOpen) }
                }
            }
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
}

private fun androidx.compose.foundation.lazy.LazyListScope.folderItems(
    folder: AppFolder, byPackage: Map<String, LaunchableApp>, open: androidx.compose.runtime.MutableState<Set<String>>, onOpen: (String) -> Unit,
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
            Icon(if (expanded) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore, contentDescription = null, tint = Color(0x99FFFFFF), modifier = Modifier.size(28.dp))
        }
    }
    if (expanded) {
        folder.packages.mapNotNull { byPackage[it] }.sortedBy { it.name.lowercase() }.forEach { inside ->
            item(key = "f-" + folder.id + "-" + inside.packageName) { Box(Modifier.padding(start = 28.dp)) { AppRow(inside, onOpen) } }
        }
    }
}

@Composable
private fun AppRow(app: LaunchableApp, onOpen: (String) -> Unit) {
    Row(Modifier.fillMaxWidth().clickable { onOpen(app.packageName) }.padding(vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        app.icon?.let { icon ->
            Image(icon.toBitmap(96, 96).asImageBitmap(), contentDescription = null, modifier = Modifier.size(32.dp))
            Spacer(Modifier.size(14.dp))
        }
        Text(app.name, color = Color.White, fontSize = 20.sp)
    }
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
