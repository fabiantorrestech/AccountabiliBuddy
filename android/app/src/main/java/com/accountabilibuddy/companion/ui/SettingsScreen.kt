package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TimePicker
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.curbox.CurboxStatus
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.engine.DayBarBuilder
import com.accountabilibuddy.companion.engine.SleepSchedule
import com.accountabilibuddy.companion.link.LinkStatus
import com.accountabilibuddy.companion.link.LinkedPc
import com.accountabilibuddy.companion.model.SleepSettings
import com.accountabilibuddy.companion.model.SleepTimes
import com.accountabilibuddy.shared.WatchLinkSettings
import com.accountabilibuddy.shared.WatchNode
import java.time.Duration
import java.time.LocalDate
import java.time.LocalTime

/** Import from the PC, sleep times, the timeline notification and bedtime reminder, Health Connect. */
@Composable
fun SettingsScreen(
    state: CompanionState,
    clock: ClockFormat,
    health: HealthStatus,
    importing: Boolean,
    onImport: () -> Unit,
    onClearImport: () -> Unit,
    onSaveSleep: (SleepSettings) -> Unit,
    onTimelineNotification: (Boolean) -> Unit,
    onBedtimeReminder: (Boolean) -> Unit,
    onConnectHealth: () -> Unit,
    onAddWidget: () -> Unit,
    curbox: CurboxStatus,
    onConnectCurbox: () -> Unit,
    noise: com.accountabilibuddy.companion.noise.NoisePlayer? = null,
    noiseSettings: com.accountabilibuddy.companion.noise.NoiseSettings = com.accountabilibuddy.companion.noise.NoiseSettings(),
    pause: com.accountabilibuddy.companion.pause.PauseState? = null,
    pauseActions: PauseActions = PauseActions(),
    onAllowUsage: () -> Unit = {},
    onOpenSetup: () -> Unit = {},
    /** Sets when Workdays' (false) or Off-Days' (true) blocks come back on (ADR 37); null when saved, else why not. */
    onReenableTime: (Boolean, java.time.LocalTime) -> String? = { _, _ -> null },
    /** The wake-up alarm rows (from the Devices tab's sync settings), shown on the Sleep page. */
    wakeAlarm: @Composable () -> Unit = {},
    /** "On · Clock" or "Off": the Wake-up alarm card's summary. */
    wakeAlarmSummary: String = "",
    /** "Drive synced 2m ago · 1 PC": the Devices & sync row's status. */
    devicesSummary: String = "",
    onOpenDevices: () -> Unit = {},
) {
    // A list of categories, each opening its own page; Back returns to the list.
    var category by rememberSaveable { mutableStateOf<SettingsCategory?>(null) }
    androidx.activity.compose.BackHandler(enabled = category != null) { category = null }
    val context = androidx.compose.ui.platform.LocalContext.current
    val usageAccess = remember(category) { com.accountabilibuddy.companion.usage.PhoneUsageReader(context).hasAccess() }
    val healthOk = health == HealthStatus.Connected
    val healthNeed = Need("Health Connect", healthOk || health == HealthStatus.NotAvailable, onConnectHealth)
    val usageNeed = Need("Usage access", usageAccess, onAllowUsage)

    Column {
    category?.let { open -> Box(Modifier.padding(start = 8.dp, top = 8.dp)) { CategoryHeader(open) { category = null } } }
    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        when (val open = category) {
            null -> {
                val sleep = state.sleep
                val summaries = mapOf(
                    SettingsCategory.Sleep to listOfNotNull(
                        if (sleep.enabled) "Sleep mode on" else "Sleep mode off",
                        "smart bedtime on".takeIf { sleep.smartWorkdays || sleep.smartOffDays },
                        "reminders".takeIf { sleep.reminders.on },
                    ).joinToString(" · "),
                    SettingsCategory.PhoneGuard to listOfNotNull(
                        pause?.let { if (it.pausedAt != null) "Paused" else "Guarding" },
                        "work card".takeIf { pauseActions.takeoverOn },
                        "Curbox".takeIf { curbox.granted },
                    ).joinToString(" · "),
                    SettingsCategory.Devices to devicesSummary,
                    SettingsCategory.Notification to if (state.timelineNotificationOn) "Timeline pinned" else "Timeline off",
                    SettingsCategory.Sound to (if (noise != null) "Noise player · headphones" else "Headphones"),
                    SettingsCategory.Permissions to permissionsSummary(context, health),
                    SettingsCategory.App to "Look · startup · backup",
                )
                SettingsCategory.entries.forEach { entry ->
                    item(key = entry.name) {
                        CategoryRow(entry, summaries[entry].orEmpty()) {
                            if (entry == SettingsCategory.Devices) onOpenDevices() else category = entry
                        }
                    }
                }
                item {
                    Hint(
                        "Local only: the app talks only to your linked PC, on your own network and encrypted, to your linked watch " +
                            "(through Google's encrypted relay only if you allow it in Devices) and, with Google Drive on, to the app's own Drive folder. Nothing else goes to the internet.",
                        label = "Privacy: what leaves this phone",
                    )
                }
            }
            else -> {
                when (open) {
                    SettingsCategory.Sleep -> {
                        item { SleepSection(state.sleep, clock, onSaveSleep) }
                        item {
                            Section("Wake-up alarm", summary = wakeAlarmSummary) { wakeAlarm() }
                        }
                        item {
                            SmartBedtimeSection(clock) { if (state.sleep.smartWorkdays || state.sleep.smartOffDays) NeedsLine(listOf(healthNeed, usageNeed), forWhat = "Smart bedtime") }
                        }
                        item { BedtimeRemindersSection() }
                        item {
                            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                                NeedsLine(listOf(usageNeed), forWhat = "Bedtime essentials")
                                EssentialsSection(com.accountabilibuddy.companion.essentials.EssentialsKind.Bedtime)
                            }
                        }
                        item {
                            Section("Health Connect", summary = if (health == HealthStatus.Connected) "Reading your sleep" else "Not connected") {
                                Text(
                                    when (health) {
                                        HealthStatus.Connected -> "Health Connect: reading your measured sleep."
                                        HealthStatus.NeedsPermission -> "Health Connect: not connected (the timeline's sleep strip and smart bedtime use it)."
                                        HealthStatus.NotAvailable -> "Health Connect isn't available on this phone."
                                        HealthStatus.Checking -> "Health Connect: checking…"
                                    },
                                    style = MaterialTheme.typography.bodySmall,
                                )
                                if (health == HealthStatus.NeedsPermission) {
                                    OutlinedButton(onClick = onConnectHealth) { Text("Connect Health Connect") }
                                }
                            }
                        }
                    }
                    SettingsCategory.PhoneGuard -> {
                        if (pause != null) {
                            item {
                                // Ticks the wait down while it's shown.
                                var now by remember { mutableStateOf(java.time.Instant.now()) }
                                LaunchedEffect(pause) {
                                    while (true) {
                                        now = java.time.Instant.now()
                                        kotlinx.coroutines.delay(1_000)
                                    }
                                }
                                PauseSection(pause, now, clock, pauseActions)
                            }
                        }
                        item {
                            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                                NeedsLine(listOf(usageNeed), forWhat = "Essentials")
                                EssentialsSection()
                            }
                        }
                        item { EmergencySection() }
                        item { CurboxSection(curbox, onConnectCurbox, onReenableTime) }
                    }
                    SettingsCategory.Notification -> {
                        item {
                            Section("Timeline notification", summary = if (state.timelineNotificationOn) "Pinned" else "Off", startOpen = true) {
                                ToggleRow("Timeline notification", "Always on, silent, pinned near the top", state.timelineNotificationOn, onChange = onTimelineNotification)
                                OutlinedButton(onClick = onAddWidget) { Text("Add timeline widget to home screen") }
                            }
                        }
                        item { TimelineLookSection() }
                    }
                    SettingsCategory.Sound -> {
                        noise?.let { player -> item { NoiseSection(player, noiseSettings) } }
                        item { HeadphonesSection() }
                    }
                    SettingsCategory.Permissions -> {
                        item {
                            Section("Permissions", summary = permissionsSummary(context, health), startOpen = true) {
                                PermissionsList(health, onConnectHealth, onAllowUsage)
                            }
                        }
                        item { OtherBlockersSection() }
                        item {
                            Section("Setup", summary = "The first-run questions", startOpen = true) {
                                Text("Walks through linking, permissions and the basics again; nothing is lost.", style = MaterialTheme.typography.bodySmall)
                                androidx.compose.material3.TextButton(onClick = onOpenSetup) { Text("Open the setup again") }
                                androidx.compose.material3.TextButton(onClick = onOpenDevices) { Text("Google Drive setup (Devices → Google Drive)") }
                            }
                        }
                    }
                    SettingsCategory.App -> {
                        item { AppearanceSection() }
                        item { StartupSection() }
                        item { BackupSection() }
                        item {
                            Section("Profiles from the PC", summary = if (state.hasImport) "Imported ${state.importedAt}" else "Old way: a backup file") {
                                Text(
                                    if (state.hasImport) "${state.importedFrom}; imported ${state.importedAt}." else "Nothing imported. Linking your devices brings the PC's profiles by itself.",
                                    style = MaterialTheme.typography.bodyMedium,
                                )
                                Hint("On the PC: Settings → Backup → Export, without secrets or with a password. Open the .abbackup file here. The phone keeps it to itself.")
                                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                    Button(onClick = onImport, enabled = !importing) { Text(if (importing) "Importing…" else "Import backup file") }
                                    if (state.hasImport) {
                                        OutlinedButton(onClick = onClearImport) { Text("Remove") }
                                    }
                                }
                            }
                        }
                    }
                    SettingsCategory.Devices -> Unit
                }
            }
        }
    }
    }
}

/** The noise player's folder and switches (the bar above the tabs plays it). */
@Composable
private fun NoiseSection(player: com.accountabilibuddy.companion.noise.NoisePlayer, settings: com.accountabilibuddy.companion.noise.NoiseSettings) {
    val pickFolder = androidx.activity.compose.rememberLauncherForActivityResult(androidx.activity.result.contract.ActivityResultContracts.OpenDocumentTree()) { uri ->
        uri?.let(player::chooseFolder)
    }
    val folder = settings.folderUri?.let { com.accountabilibuddy.companion.sync.DriveFolderSource.displayName(it) }
    Section("Noise player", summary = folder?.let { "Folder: $it" } ?: "No folder yet") {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(folder?.let { "Folder: $it" } ?: "No folder chosen", style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
            OutlinedButton(onClick = { pickFolder.launch(null) }) { Text(if (folder == null) "Choose folder" else "Change") }
        }
        ToggleRow("Repeat", "Play the same sound again and again", settings.repeat, onChange = player::setRepeat)
        ToggleRow(
            "Lock to headphones",
            "Plays only while headphones are connected (wired or Bluetooth), and pauses when they go. Also the lock on the player.",
            settings.headphoneLock, onChange = player::setHeadphoneLock,
        )
        ToggleRow(
            "Stop when headphones disconnect",
            "Pauses the player when headphones go, locked or not. Separate from playing when they connect.",
            settings.stopOnDisconnect, onChange = player::setStopOnDisconnect,
        )
        ToggleRow(
            "Use Android's media controls",
            "On: it shows in the media controls, headset buttons work, and it pauses when other audio plays. Off: other audio, like a YouTube video, plays over it.",
            settings.systemControls, onChange = player::setSystemControls,
        )
        ToggleRow("Play/pause on the widget", "A button on the home-screen widget", settings.widgetButton, onChange = player::setWidgetButton)
        ToggleRow("Volume buttons on the widget", "− and + on the home-screen widget", settings.widgetVolume, onChange = player::setWidgetVolume)
    }
}

@Composable
private fun CurboxSection(curbox: CurboxStatus, onConnect: () -> Unit, onReenableTime: (Boolean, java.time.LocalTime) -> String?) {
    val summary = when {
        !curbox.installed -> "Not installed"
        !curbox.granted -> "Not connected"
        curbox.focusActive -> "Focus running"
        else -> "Connected"
    }
    Section("Curbox", summary = summary) {
        when {
            !curbox.installed -> Text("Not installed (or too old for its API).", style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            !curbox.granted -> {
                Text("Allow AccountabiliBuddy in Curbox to show its status here.", style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                OutlinedButton(onClick = onConnect) { Text("Connect to Curbox") }
            }
            else -> {
                Text(
                    if (curbox.focusActive) "Focus running: ${curbox.focusGroup ?: ""}${curbox.focusRemainingMinutes?.let { ", $it min left" } ?: ""}" else "No Curbox focus running.",
                    style = MaterialTheme.typography.bodyMedium,
                )
                curbox.screenTimeMinutes?.let { Text("Screen time today (Curbox): ${it / 60}h ${it % 60}m", style = MaterialTheme.typography.bodySmall) }
                curbox.groups.forEach { group ->
                    Text(
                        "${group.kind}: ${group.name} · ${group.count} items${if (group.active == false) " · off" else ""}",
                        style = MaterialTheme.typography.bodySmall,
                    )
                }
                Hint("Importing the groups' apps and sites needs Curbox's API to share them; today it only shares counts.", label = "Why only counts")
                // ADR 37: the day plan's block rules turn Curbox blocks back on.
                val context = androidx.compose.ui.platform.LocalContext.current
                var rearm by remember { mutableStateOf(com.accountabilibuddy.companion.blocks.BlockGuard.enabled(context)) }
                ToggleRow(
                    "Turn blocks back on",
                    "A block you turn off comes back on at the day's re-enable time (midnight unless the day plan says otherwise), with a heads-up 5 minutes before.",
                    rearm,
                ) {
                    rearm = it
                    com.accountabilibuddy.companion.blocks.BlockGuard.setEnabled(context, it)
                }
                if (rearm) ReenableTimes(onReenableTime)
                com.accountabilibuddy.companion.blocks.BlockGuard.history(context).take(5).forEach {
                    Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
        curbox.error?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = Color(0xFFFCA5A5)) }
    }
}

@Composable
internal fun PcSection(status: LinkStatus, pc: LinkedPc?, onPaste: (String) -> Boolean, onUnpair: () -> Unit) {
    val summary = when {
        pc == null -> "Not linked"
        status is LinkStatus.Connected -> "${pc.pcName} · connected"
        else -> "${pc.pcName} · not connected"
    }
    Section("PC", summary = summary) {
        // The Wi-Fi link is being replaced by Google Drive sync (ADR 26); it stays until Drive has worked for a week.
        Text("The Wi-Fi link is being replaced by Google Drive sync (Devices → Google Drive): sign in there on the phone and the PC.",
            style = MaterialTheme.typography.bodySmall, color = Color(0xFFF4A261))
        if (pc == null) {
            Text("Not linked.", style = MaterialTheme.typography.bodyMedium)
            Hint(
                "On the PC: Setup → Linked devices → Turn on → Pair a phone or tablet, then scan the QR code " +
                    "with this phone's camera. If the camera only shows the link as text, copy it and paste it here.",
                label = "How to link",
            )
            var pasted by remember { mutableStateOf("") }
            var wrong by remember { mutableStateOf(false) }
            OutlinedTextField(
                value = pasted,
                onValueChange = {
                    pasted = it
                    wrong = false
                },
                label = { Text("Pairing link (accountabilibuddy://pair…)") },
                singleLine = true,
                isError = wrong,
                modifier = Modifier.fillMaxWidth(),
            )
            OutlinedButton(onClick = { wrong = !onPaste(pasted) }, enabled = pasted.isNotBlank()) { Text("Use this link") }
            if (wrong) {
                Text("That isn't a pairing link from the PC.", style = MaterialTheme.typography.bodySmall, color = Color(0xFFFCA5A5))
            }
            return@Section
        }

        val state = when (status) {
            is LinkStatus.Connected -> "connected"
            is LinkStatus.Offline -> "can't be reached right now"
            LinkStatus.Connecting -> "connecting…"
            else -> "connects while this app is open"
        }
        Text("Linked to ${pc.pcName} (${pc.host}) · $state", style = MaterialTheme.typography.bodyMedium)
        Hint("The PC's session shows on Today, where you can start one or end it early with the PC's own wait and reason.")
        OutlinedButton(onClick = onUnpair) { Text("Unpair") }
    }
}

/** The Pixel Watch link (ADR 21) as the Devices tab shows it. */
data class WatchSectionState(val settings: WatchLinkSettings = WatchLinkSettings(), val detected: List<WatchNode> = emptyList())

data class WatchSectionActions(
    val onFind: () -> Unit = {},
    val onLink: (WatchNode) -> Unit = {},
    val onUnlink: () -> Unit = {},
    val onDataLayer: (Boolean) -> Unit = {},
    val onRelay: (Boolean) -> Unit = {},
    val onWifi: (Boolean) -> Unit = {},
)

@Composable
internal fun WatchSection(watch: WatchSectionState, actions: WatchSectionActions) {
    val settings = watch.settings
    LaunchedEffect(Unit) { actions.onFind() }

    val linkedName = settings.linkedName
    val summary = if (settings.linkedNodeId != null && linkedName != null) "Linked: $linkedName" else "Not linked"
    Section("Pixel Watch", summary = summary) {
        if (settings.linkedNodeId != null && linkedName != null) {
            Text("Linked: $linkedName.", style = MaterialTheme.typography.bodyMedium)
            OutlinedButton(onClick = actions.onUnlink) { Text("Unlink") }
        } else if (watch.detected.isEmpty()) {
            Text("No watch found.", style = MaterialTheme.typography.bodyMedium)
            Hint("Install the watch app and keep the watch connected to this phone, then check again.", label = "How to find it")
            OutlinedButton(onClick = actions.onFind) { Text("Check again") }
        } else {
            watch.detected.forEach { found ->
                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                    Column(Modifier.weight(1f)) {
                        Text(found.name, style = MaterialTheme.typography.bodyLarge)
                        Text(if (found.isNearby) "Nearby" else "Not nearby right now", style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    Button(onClick = { actions.onLink(found) }) { Text("Link this watch") }
                }
            }
        }

        Text("How it may reach the watch", style = MaterialTheme.typography.bodyMedium)
        ToggleRow("Through this phone", "Over the watch's own connection to the phone (Wear OS Data Layer).", settings.dataLayerOn, onChange = actions.onDataLayer)
        ToggleRow(
            "Google's encrypted relay",
            "When the watch is away from the phone's Bluetooth, Google's servers pass it along, end-to-end encrypted so Google can't " +
                "read it. Off: only while the watch is near the phone.",
            settings.relayOn && settings.dataLayerOn, enabled = settings.dataLayerOn, onChange = actions.onRelay,
        )
        ToggleRow(
            "Wi-Fi straight to the PC",
            "When the phone can't be reached, the watch asks the PC itself on your Wi-Fi (pair it from the PC's QR code with " +
                "\"Pair my watch\").",
            settings.wifiFallbackOn, onChange = actions.onWifi,
        )
        Hint(
            "Nothing goes to a watch until you link it. The watch gets plan parts, focus session times and profile names, drift times, " +
                "totals and the daily goal. Never page titles, addresses, reasons or notes.",
            label = "What the watch gets",
        )
    }
}

/**
 * A titled group. With a [summary] it folds: the title and the one-line summary show, the rest opens on a tap
 * ([startOpen] for the first look). Without one it's always open.
 */
@Composable
internal fun Section(title: String, summary: String? = null, startOpen: Boolean = false, content: @Composable () -> Unit) {
    var open by rememberSaveable(title) { mutableStateOf(summary == null || startOpen) }
    // Each section on its own tinted card, so one is easy to tell from the next.
    androidx.compose.material3.Surface(
        shape = androidx.compose.foundation.shape.RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.surfaceContainerLow,
        modifier = Modifier.fillMaxWidth(),
    ) {
    Column(Modifier.padding(horizontal = 16.dp, vertical = 14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            modifier = Modifier.fillMaxWidth().clickable(enabled = summary != null) { open = !open },
        ) {
            Column(Modifier.weight(1f)) {
                IconTitle(title, style = MaterialTheme.typography.titleMedium)
                if (summary != null && !open) {
                    Text(summary, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1,
                        overflow = TextOverflow.Ellipsis)
                }
            }
            if (summary != null) {
                androidx.compose.material3.Icon(
                    if (open) androidx.compose.material.icons.Icons.Outlined.ExpandLess else androidx.compose.material.icons.Icons.Outlined.ExpandMore,
                    contentDescription = if (open) "Fold" else "Open",
                    tint = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.size(32.dp),
                )
            }
        }
        // Folds open and shut smoothly (Settings → Appearance → Animations; at once when off).
        val scale = LocalMotionScale.current
        androidx.compose.animation.AnimatedVisibility(
            visible = open,
            enter = androidx.compose.animation.expandVertically(motionSpec(scale, 220)) + androidx.compose.animation.fadeIn(motionSpec(scale, 220)),
            exit = androidx.compose.animation.shrinkVertically(motionSpec(scale, 180)) + androidx.compose.animation.fadeOut(motionSpec(scale, 180)),
        ) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) { content() }
        }
    }
    }
}

/** Settings → Appearance: the theme and how fast animations run. */
@Composable
private fun AppearanceSection() {
    val context = androidx.compose.ui.platform.LocalContext.current
    val theme by UiPrefs.theme.collectAsState()
    val motion by UiPrefs.motion.collectAsState()
    Section("Appearance", summary = "${theme.label} · animations ${motion.label.lowercase()}") {
        Text("Theme", style = MaterialTheme.typography.bodyMedium)
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            ThemeChoice.entries.forEach { choice ->
                androidx.compose.material3.FilterChip(selected = theme == choice, onClick = { UiPrefs.setTheme(context, choice) }, label = { Text(choice.label) })
            }
        }
        Text("Animations", style = MaterialTheme.typography.bodyMedium)
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            MotionSpeed.entries.forEach { speed ->
                androidx.compose.material3.FilterChip(selected = motion == speed, onClick = { UiPrefs.setMotion(context, speed) }, label = { Text(speed.label) })
            }
        }
    }
}

/** A switch with its title; a short [detail] shows under it, a long one behind ⓘ. */
@Composable
internal fun ToggleRow(title: String, detail: String, checked: Boolean, enabled: Boolean = true, onChange: (Boolean) -> Unit) {
    var explained by remember { mutableStateOf(false) }
    val long = detail.length > SHORT_TEXT
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(title, style = MaterialTheme.typography.bodyLarge)
                if (long) {
                    Text(" ⓘ", style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.clickable { explained = !explained })
                }
            }
            if (!long || explained) {
                Text(detail, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        Switch(checked = checked, onCheckedChange = onChange, enabled = enabled)
    }
}

/** An explanation folded behind "ⓘ [label]"; a tap shows the [text]. */
@Composable
internal fun Hint(text: String, label: String = "How this works") {
    var open by remember { mutableStateOf(false) }
    Column {
        Text("ⓘ $label", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary,
            modifier = Modifier.clickable { open = !open }.padding(vertical = 2.dp))
        if (open) {
            Text(text, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

/** Longer than this, a switch's detail goes behind ⓘ. */
private const val SHORT_TEXT = 48

/** Which time the picker is editing. */
private enum class SleepField { WeeknightBed, WeeknightWake, WeekendBed, WeekendWake }

@Composable
private fun SleepSection(saved: SleepSettings, clock: ClockFormat, onSave: (SleepSettings) -> Unit) {
    var editing by remember { mutableStateOf<SleepField?>(null) }

    val summary = if (!saved.enabled) "Off" else "Bed ${clock.time(LocalDate.now().atTime(saved.weeknights.bedtime))} · up ${clock.time(LocalDate.now().atTime(saved.weeknights.wake))}"
    Section("Sleep times", summary = summary) {
        Hint("The bar runs from wake time to bedtime and shades the night; from two hours before bedtime it says how much sleep you'll get if you go now. Comes from the PC: over the link when it's paired, or from a backup.")
        ToggleRow("Sleep mode", "Bedtime and wake time on the timeline", saved.enabled) { onSave(saved.copy(enabled = it)) }
        ToggleRow("Same every night", "Off: Friday and Saturday nights use the weekend times", saved.sameEveryNight) { onSave(saved.copy(sameEveryNight = it)) }
        NightRow(if (saved.sameEveryNight) "Every night" else "Weeknights (Sun–Thu)", saved.weeknights, clock,
            onBed = { editing = SleepField.WeeknightBed }, onWake = { editing = SleepField.WeeknightWake })
        if (!saved.sameEveryNight) {
            NightRow("Weekend nights (Fri–Sat)", saved.weekends, clock,
                onBed = { editing = SleepField.WeekendBed }, onWake = { editing = SleepField.WeekendWake })
        }
    }

    editing?.let { field ->
        val initial = when (field) {
            SleepField.WeeknightBed -> saved.weeknights.bedtime
            SleepField.WeeknightWake -> saved.weeknights.wake
            SleepField.WeekendBed -> saved.weekends.bedtime
            SleepField.WeekendWake -> saved.weekends.wake
        }
        TimeDialog(initial, clock.is24Hour, onDismiss = { editing = null }) { picked ->
            editing = null
            onSave(
                when (field) {
                    SleepField.WeeknightBed -> saved.copy(weeknights = saved.weeknights.copy(bedtime = picked))
                    SleepField.WeeknightWake -> saved.copy(weeknights = saved.weeknights.copy(wake = picked))
                    SleepField.WeekendBed -> saved.copy(weekends = saved.weekends.copy(bedtime = picked))
                    SleepField.WeekendWake -> saved.copy(weekends = saved.weekends.copy(wake = picked))
                },
            )
        }
    }
}

@Composable
private fun NightRow(label: String, times: SleepTimes, clock: ClockFormat, onBed: () -> Unit, onWake: () -> Unit) {
    val night = SleepSchedule(times, times).night(LocalDate.of(2026, 1, 5))
    val length = Duration.between(night.start, night.end)
    val suspicious = length < Duration.ofHours(3) || length > Duration.ofHours(16)
    Column {
        Text(label, style = MaterialTheme.typography.bodyMedium)
        Row(verticalAlignment = Alignment.CenterVertically) {
            OutlinedButton(onClick = onBed) { Text("Bed ${clock.time(LocalDate.now().atTime(times.bedtime))}") }
            Spacer(Modifier.width(8.dp))
            OutlinedButton(onClick = onWake) { Text("Up ${clock.time(LocalDate.now().atTime(times.wake))}") }
            Spacer(Modifier.width(10.dp))
            Text(
                if (suspicious) "⚠ ${DayBarBuilder.duration(length)}: check AM/PM" else "${DayBarBuilder.duration(length)} of sleep",
                color = if (suspicious) Color(0xFFFCA5A5) else MaterialTheme.colorScheme.onSurfaceVariant,
                style = MaterialTheme.typography.bodySmall,
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TimeDialog(initial: LocalTime, is24Hour: Boolean, onDismiss: () -> Unit, onPick: (LocalTime) -> Unit) {
    val picker = rememberTimePickerState(initialHour = initial.hour, initialMinute = initial.minute, is24Hour = is24Hour)
    AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = { TextButton(onClick = { onPick(LocalTime.of(picker.hour, picker.minute)) }) { Text("OK") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
        text = { TimePicker(state = picker) },
    )
}
