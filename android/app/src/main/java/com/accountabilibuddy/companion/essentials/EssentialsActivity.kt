package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.drawable.Drawable
import android.app.KeyguardManager
import android.media.AudioManager
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import com.accountabilibuddy.companion.nudge.Takeover
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.isImeVisible
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
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
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
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.graphics.drawable.toBitmap
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.CompanionTheme
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * The essentials screen (in LockMyPhone-Extended's style; soft: nothing is blocked): during a session on another device,
 * work time or emergency phone time, unlocking lands here. Black, a big clock and the time left, the shown folders
 * (open on a tap, shut again when an app opens or the phone locks) and the essentials in the order arranged in
 * Settings, the Phone shortcut in a bottom corner (or in the list), and a faint search icon in the other bottom corner,
 * like LockMyPhone-Extended's allowed-apps list: no text box, the typed text shows across the bottom above the
 * keyboard, and Enter shows only near-exact names ([AppMatch]) just above it. Back or a tap anywhere else cancels. With [EXTRA_ARRANGE] it's the same screen with ↑ ↓ on every row, folder and app
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
        val kind = intent.getStringExtra(EXTRA_KIND)?.let { name -> EssentialsKind.entries.firstOrNull { it.name == name } } ?: EssentialsKind.Focus
        // A test (Settings → Essentials → Try it for 1 minute) closes by itself when its minute is up.
        val testUntil = intent.getLongExtra(EXTRA_TEST_UNTIL, 0L).takeIf { it > 0 }?.let(Instant::ofEpochMilli)
        testUntil?.let { until ->
            window.decorView.postDelayed({ finish() }, Duration.between(Instant.now(), until).toMillis().coerceAtLeast(0))
        }
        setContent {
            CompanionTheme {
                androidx.compose.runtime.key(resetKey) {
                    EssentialsScreen(app, kind, arranging, onOpen = ::openApp, onDial = ::dial, onDone = ::finish, testUntil = testUntil)
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        (application as CompanionApp).essentialsOnScreen = true
        reopenHandler.removeCallbacksAndMessages(null)
        openedFromScreen = false
        lunchCheck.run()
    }

    // The focus screen closes when a lunch starts (from any device): lunch is a break from the session.
    private val lunchCheck: Runnable = object : Runnable {
        override fun run() {
            val kind = intent.getStringExtra(EXTRA_KIND)?.let { name -> EssentialsKind.entries.firstOrNull { it.name == name } } ?: EssentialsKind.Focus
            val ownChoice = intent.getBooleanExtra(EXTRA_ARRANGE, false) || intent.getLongExtra(EXTRA_TEST_UNTIL, 0L) > 0
            if (kind == EssentialsKind.Focus && !ownChoice && (application as CompanionApp).lunch.isOnLunch(Instant.now())) {
                finish()
                return
            }

            window.decorView.removeCallbacks(this)
            window.decorView.postDelayed(this, LUNCH_CHECK_MS)
        }
    }

    override fun onStop() {
        super.onStop()
        window.decorView.removeCallbacks(lunchCheck)
        (application as CompanionApp).essentialsOnScreen = false
        resetKey++
        scheduleReopen()
    }

    private val reopenHandler = Handler(Looper.getMainLooper())
    // An essential app (or the dialer) was opened from this screen: leaving for it is not leaving, until that app is left too.
    private var openedFromScreen = false

    /**
     * Home (or anything else that hides the screen) during a focus session: brings it back after about 3 s. After an
     * essential app was opened from it: with Usage access, the foreground app is checked every ~3 s and the screen comes
     * back once the app in front isn't an essential (the launcher counts as not); without it, 5 minutes after leaving.
     * Not for the arrange view, the 1-minute test, the bedtime screen, or a screen that is closing.
     */
    private fun scheduleReopen() {
        val kind = intent.getStringExtra(EXTRA_KIND)?.let { name -> EssentialsKind.entries.firstOrNull { it.name == name } } ?: EssentialsKind.Focus
        if (kind != EssentialsKind.Focus || isFinishing || intent.getBooleanExtra(EXTRA_ARRANGE, false) || intent.getLongExtra(EXTRA_TEST_UNTIL, 0L) > 0) return
        val reopenIntent = EssentialsActivity.intent(this, kind)
        reopenHandler.removeCallbacksAndMessages(null)
        if (PhoneUsageReader(this).hasAccess()) {
            reopenHandler.postDelayed(object : Runnable {
                override fun run() {
                    val app = application as CompanionApp
                    // The session ended, or the screen is back: stop.
                    if (EssentialsLaunch.runningSessionStart(app, Instant.now()) == null || app.essentialsOnScreen) return
                    if (shouldReopen()) Takeover.startFromBackground(this@EssentialsActivity, reopenIntent)
                    else reopenHandler.postDelayed(this, REOPEN_DELAY_MS)
                }
            }, REOPEN_DELAY_MS)
        } else {
            val delay = if (openedFromScreen) NO_USAGE_REOPEN_DELAY_MS else REOPEN_DELAY_MS
            reopenHandler.postDelayed({ if (shouldReopen()) Takeover.startFromBackground(this, reopenIntent) }, delay)
        }
    }

    private fun shouldReopen(): Boolean {
        val app = application as CompanionApp
        val now = Instant.now()
        // Lunch is a break from the session: the screen doesn't come back during it.
        val running = EssentialsLaunch.runningSessionStart(app, now) != null && !app.lunch.isOnLunch(now)
        // Phone locked or screen off, an emergency, a call (ringing too), or an allowed app in front: leave it alone.
        val interactive = getSystemService(PowerManager::class.java).isInteractive && !getSystemService(KeyguardManager::class.java).isKeyguardLocked
        val inCall = getSystemService(AudioManager::class.java).mode.let {
            it == AudioManager.MODE_IN_CALL || it == AudioManager.MODE_IN_COMMUNICATION || it == AudioManager.MODE_RINGTONE
        }
        val inFront = PhoneUsageReader(this).foregroundApp(now)
        val allowedInFront = inFront != null && app.essentialsOf(EssentialsKind.Focus).isAllowed(inFront.first, inFront.second)
        // A work use ("Need it for work") lets other apps be used for its minutes.
        return running && interactive && !app.emergency.isActive(now) && app.workUse.active(now) == null && !inCall && !allowedInFront && !app.essentialsOnScreen
    }

    override fun onDestroy() {
        reopenHandler.removeCallbacksAndMessages(null)
        super.onDestroy()
    }

    private fun openApp(packageName: String) {
        packageManager.getLaunchIntentForPackage(packageName)?.let { intent ->
            if (runCatching { startActivity(intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }.isSuccess) openedFromScreen = true
        }
    }

    private fun dial() {
        if (runCatching { startActivity(Intent(Intent.ACTION_DIAL).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }.isSuccess) openedFromScreen = true
    }

    companion object {
        private const val REOPEN_DELAY_MS = 3_000L
        private const val LUNCH_CHECK_MS = 5_000L
        private const val NO_USAGE_REOPEN_DELAY_MS = 5 * 60_000L
        const val EXTRA_ARRANGE = "arrange"
        const val EXTRA_TEST_UNTIL = "testUntil"
        /** Which list: an [EssentialsKind] name (Focus when missing). */
        const val EXTRA_KIND = "kind"

        /** Opens the essentials screen of [kind] (from the work card, emergency time, or an unlock during a session or the night). */
        fun intent(context: Context, kind: EssentialsKind = EssentialsKind.Focus): Intent =
            Intent(context, EssentialsActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
                .putExtra(EXTRA_KIND, kind.name)

        /** The same screen to arrange (Settings → Essentials, Settings → Sleep → Bedtime essentials). */
        fun arrangeIntent(context: Context, kind: EssentialsKind = EssentialsKind.Focus): Intent = intent(context, kind).putExtra(EXTRA_ARRANGE, true)

        /** The screen as it is in a session (or the night), for one minute: to try it out. */
        fun testIntent(context: Context, kind: EssentialsKind = EssentialsKind.Focus): Intent =
            intent(context, kind).putExtra(EXTRA_TEST_UNTIL, Instant.now().plusSeconds(60).toEpochMilli())
    }
}

/** A launchable app: package, name, icon (null until loaded or with icons off). */
data class LaunchableApp(val packageName: String, val name: String, val icon: Drawable?)

/** Whether rows show app icons (Settings → Essentials → App icons). */
private val LocalShowIcons = androidx.compose.runtime.compositionLocalOf { true }

@Composable
private fun EssentialsScreen(app: CompanionApp, kind: EssentialsKind, arranging: Boolean, onOpen: (String) -> Unit, onDial: () -> Unit, onDone: () -> Unit, testUntil: Instant? = null) {
    val context = androidx.compose.ui.platform.LocalContext.current
    val store = app.essentialsOf(kind)
    val essentials by store.essentials.collectAsState()
    var corner by remember { mutableStateOf(store.phoneCorner) }
    // Read off the main thread (it froze the screen); the clock and the Phone button show at once.
    var all by remember { mutableStateOf(emptyList<LaunchableApp>()) }
    LaunchedEffect(Unit) { all = AppCatalog.apps(context) }
    val byPackage = remember(all) { all.associateBy { it.packageName } }
    val showIcons = remember { store.showIcons }
    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(Unit) {
        while (true) {
            now = Instant.now()
            kotlinx.coroutines.delay(1_000)
        }
    }
    var searching by remember { mutableStateOf(false) }
    // Back does nothing here (the way out is ending the session); the arrange view and the 1-minute test still close.
    if (!arranging && testUntil == null && kind == EssentialsKind.Focus) BackHandler {}
    val open = remember { mutableStateOf(setOf<String>()) }
    val keys = essentials.homeKeys { byPackage[it]?.name ?: it }
    val moves = if (arranging) Moves(store, keys) else null
    // Hidden apps never show on the screen, not even inside a folder: only the search finds them. Only essentials show.
    val hidden = essentials.apps.filter { it.hidden }.map { it.packageName }.toSet()
    val essentialPackages = essentials.apps.map { it.packageName }.toSet()

    androidx.compose.runtime.CompositionLocalProvider(LocalShowIcons provides showIcons) {
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
                Column(Modifier.weight(1f)) {
                    // 12-hour with AM/PM and the date under it, whatever the system's 24-hour setting.
                    val time = LocalDateTime.ofInstant(now, ZoneId.systemDefault())
                    Text(time.format(DateTimeFormatter.ofPattern("h:mm a", Locale.US)), color = Color.White, fontSize = 56.sp)
                    Text(time.format(DateTimeFormatter.ofPattern("EEEE, MMMM d", Locale.US)), color = Color(0xCCFFFFFF), fontSize = 16.sp)
                }
                (testUntil?.let { "Test · ${Duration.between(now, it).seconds.coerceAtLeast(0)} s left" } ?: timeLeft(app, kind, now))?.let { Text(it, color = Color(0xCCFFFFFF), fontSize = 16.sp, modifier = Modifier.padding(top = 18.dp)) }
            }
            LazyColumn(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                item { Spacer(Modifier.height(40.dp)) }
                if (corner == "list") {
                    item { PhoneRow(onDial) }
                }
                keys.forEach { key ->
                    when {
                        key.startsWith("folder:") -> essentials.folders.firstOrNull { Essentials.folderKey(it.id) == key }
                            ?.let { folder -> folderItems(folder, byPackage, open, onOpen, moves, hidden, essentialPackages) }
                        else -> byPackage[key.removePrefix("app:")]?.let { found ->
                            item(key = key) { AppRow(found, onOpen, moves?.let { m -> { by: Int -> m.row(key, by) } }) }
                        }
                    }
                }
                // Room for the corner buttons, so the last row stays reachable.
                item { Spacer(Modifier.height(96.dp)) }
            }
        }
        // The Phone shortcut in a bottom corner.
        if (corner != "list") {
            Surface(
                shape = CircleShape, color = Color(0xFF1F2937),
                modifier = Modifier.align(if (corner == "left") Alignment.BottomStart else Alignment.BottomEnd)
                    .padding(24.dp).size(60.dp).clickable(onClick = onDial),
            ) {
                Box(contentAlignment = Alignment.Center) { Icon(Icons.Outlined.Call, contentDescription = "Phone", tint = Color.White, modifier = Modifier.size(30.dp)) }
            }
        }
        if (!arranging) {
            val searchCorner = if (corner == "left") Alignment.BottomEnd else Alignment.BottomStart
            if (!searching) {
                // Faint on purpose: it's there when you look for it, not an invitation.
                Box(
                    contentAlignment = Alignment.Center,
                    modifier = Modifier.align(searchCorner).padding(24.dp).size(60.dp).clickable { searching = true },
                ) {
                    Icon(Icons.Outlined.Search, contentDescription = "Search", tint = Color(0x2EFFFFFF), modifier = Modifier.size(28.dp))
                }
            } else {
                SearchBand(
                    folders = essentials.folders,
                    // Only your essentials (hidden ones included) and folders: anything else isn't on this screen.
                    apps = all.filter { it.packageName in essentialPackages },
                    onOpenFolder = { folder -> open.value = open.value + folder.id; searching = false },
                    onOpen = onOpen,
                    onCancel = { searching = false },
                )
            }
        }
    }
    }
}

/**
 * The open search: a tap anywhere outside it cancels; the typed text shows across the bottom on a black band (no text
 * box), and Enter shows the near-exact matches just above it. Back cancels too, and so does closing the keyboard (that's
 * what the first Back does while it's up), so one Back is enough.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun BoxScope.SearchBand(
    folders: List<AppFolder>,
    apps: List<LaunchableApp>,
    onOpenFolder: (AppFolder) -> Unit,
    onOpen: (String) -> Unit,
    onCancel: () -> Unit,
) {
    var query by remember { mutableStateOf("") }
    var searched by remember { mutableStateOf<String?>(null) }
    val focus = remember { FocusRequester() }
    BackHandler(onBack = onCancel)
    val imeVisible = WindowInsets.isImeVisible
    var imeSeen by remember { mutableStateOf(false) }
    LaunchedEffect(imeVisible) { if (imeVisible) imeSeen = true else if (imeSeen) onCancel() }
    LaunchedEffect(Unit) { focus.requestFocus() }

    Box(Modifier.matchParentSize().clickable(interactionSource = remember { MutableInteractionSource() }, indication = null, onClick = onCancel))
    Column(Modifier.align(Alignment.BottomCenter).fillMaxWidth().background(Color.Black).padding(horizontal = 24.dp, vertical = 12.dp)) {
        searched?.let { text ->
            val foundFolders = folders.filter { AppMatch.matches(text, it.name) }
            val foundApps = apps.filter { AppMatch.matches(text, it.name) }
            if (foundFolders.isEmpty() && foundApps.isEmpty()) {
                Text(
                    "Nothing called \"$text\". Type its name.", color = Color(0x99FFFFFF), textAlign = TextAlign.Center,
                    modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp),
                )
            }
            foundFolders.forEach { folder ->
                Row(Modifier.fillMaxWidth().clickable { onOpenFolder(folder) }.padding(vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Outlined.Folder, contentDescription = null, tint = Color.White, modifier = Modifier.size(32.dp))
                    Spacer(Modifier.size(14.dp))
                    Text(folder.name, color = Color.White, fontSize = 20.sp)
                }
            }
            foundApps.forEach { found -> AppRow(found, onOpen, null) }
        }
        BasicTextField(
            value = query,
            onValueChange = { query = it; searched = null },
            singleLine = true,
            textStyle = TextStyle(color = Color.White, fontSize = 22.sp, textAlign = TextAlign.Center),
            cursorBrush = SolidColor(Color(0xFF7DD3C0)),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { searched = query.trim().takeIf { it.isNotEmpty() } }),
            decorationBox = { field ->
                Box(contentAlignment = Alignment.Center) {
                    if (query.isEmpty()) Text("Type an app's name, then Enter", color = Color(0x66FFFFFF), fontSize = 16.sp)
                    field()
                }
            },
            modifier = Modifier.fillMaxWidth().focusRequester(focus),
        )
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
    hidden: Set<String>, essentials: Set<String>,
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
        folder.packages.filter { it in essentials && it !in hidden }.mapNotNull { byPackage[it] }.forEach { inside ->
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
        if (LocalShowIcons.current) {
            // Loaded off the main thread as the row shows.
            val context = androidx.compose.ui.platform.LocalContext.current
            val icon by androidx.compose.runtime.produceState<android.graphics.drawable.Drawable?>(null, app.packageName) { value = AppCatalog.icon(context, app.packageName) }
            Box(Modifier.size(32.dp)) {
                icon?.let { Image(it.toBitmap(96, 96).asImageBitmap(), contentDescription = null, modifier = Modifier.size(32.dp)) }
            }
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

// The time left: the emergency time, else the night's wake time (bedtime list), else the session on another device
// (from its sync file), else the phone's test session, else nothing.
private fun timeLeft(app: CompanionApp, kind: EssentialsKind, now: Instant): String? {
    app.emergency.active(now)?.let { return "Phone time · ${minutes(Duration.between(now, it.end))} left" }
    if (kind == EssentialsKind.Bedtime) {
        val night = BedtimeNight.current(app) ?: return null
        val wake = night.end.atZone(java.time.ZoneId.systemDefault()).toInstant()
        return "Up at ${night.end.format(DateTimeFormatter.ofPattern("h:mm a", Locale.US))} · ${minutes(Duration.between(now, wake))}"
    }
    val running = app.sync.received.value
        .filter { it.file.device.kind == com.accountabilibuddy.shared.sync.SyncDeviceCard.KIND_PC }
        .mapNotNull { it.file.running }
        .filter { it.phaseEndsAt?.isAfter(now) ?: true }
        .maxByOrNull { it.phaseStartedAt }
        ?: return app.testSession.running(now)?.let { (_, end) -> "Test session · ${minutes(Duration.between(now, end))} left" }
    return running.phaseEndsAt?.let { "${if (running.isBreak) "Break" else "Focus"} · ${minutes(Duration.between(now, it))} left" } ?: "Session running"
}

private fun minutes(left: Duration): String {
    val m = left.toMinutes().coerceAtLeast(0)
    return if (m >= 60) "${m / 60}h ${"%02d".format(m % 60)}m" else "${m}m"
}
