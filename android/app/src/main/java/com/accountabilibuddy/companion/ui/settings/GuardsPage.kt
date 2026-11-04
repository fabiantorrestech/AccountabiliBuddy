package com.accountabilibuddy.companion.ui.settings

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Apps
import androidx.compose.material.icons.outlined.Computer
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material.icons.outlined.PhoneAndroid
import androidx.compose.material.icons.outlined.Smartphone
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.curbox.CurboxStatus
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.pause.PauseRules
import com.accountabilibuddy.companion.pause.PauseState
import com.accountabilibuddy.companion.pause.PauseStatus
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.companion.sync.PcState
import com.accountabilibuddy.companion.ui.rememberTickingNow
import com.accountabilibuddy.companion.ui.CurboxSetupPage
import com.accountabilibuddy.companion.ui.EmergencySection
import com.accountabilibuddy.companion.ui.EssentialsSection
import com.accountabilibuddy.companion.ui.GuardsRestBanner
import com.accountabilibuddy.companion.ui.Need
import com.accountabilibuddy.companion.ui.NeedsLine
import com.accountabilibuddy.companion.ui.Note
import com.accountabilibuddy.companion.ui.PauseActions
import com.accountabilibuddy.companion.ui.PauseAdvancedControls
import com.accountabilibuddy.companion.ui.PauseSection
import com.accountabilibuddy.companion.ui.PhoneDownSection
import com.accountabilibuddy.companion.ui.PhoneGuardCards
import com.accountabilibuddy.companion.ui.PhoneGuards
import com.accountabilibuddy.companion.ui.PlacementSection
import com.accountabilibuddy.companion.ui.ProfileCard
import com.accountabilibuddy.companion.ui.ProfileDeviceRow
import com.accountabilibuddy.companion.ui.Section
import com.accountabilibuddy.companion.ui.SectionIcons
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbListRow
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import java.time.Instant
import java.time.ZoneId

/** The places inside Settings › Guards: the list, then the phone guard and, one level further, Essentials and the Curbox setup. */
private enum class GuardsView(val title: String, val crumb: String) {
    List("Guards", "Settings"),
    PhoneGuard("Phone guard", "Guards"),
    Essentials("Essentials", "Phone guard"),
    CurboxSetup("Curbox setup", "Phone guard");

    /** Where Back goes (the list's own Back leaves Guards). */
    val parent: GuardsView? get() = when (this) {
        List -> null
        PhoneGuard -> List
        Essentials, CurboxSetup -> PhoneGuard
    }
}

// The view a search result (a row's key) opens on; anything else opens the list.
private fun viewFor(anchor: String?): GuardsView = when (anchor) {
    SettingsAnchors.PHONE_GUARD, SettingsAnchors.PHONE_DOWN, SettingsAnchors.PLACEMENT, SettingsAnchors.EMERGENCY, SettingsAnchors.ADVANCED -> GuardsView.PhoneGuard
    SettingsAnchors.ESSENTIALS, SettingsAnchors.LOCKMYPHONE -> GuardsView.Essentials
    SettingsAnchors.CURBOX_SETUP -> GuardsView.CurboxSetup
    else -> GuardsView.List
}

/**
 * Settings › Guards (boards 5i and 17b). The list has the Phone guard page and the PC's profiles (read only); the Phone guard page the
 * master switch, Phone down on unlock, Essentials, Sense where the phone is, emergency phone time and an Advanced fold; Essentials and
 * the Curbox setup are pages below it. While a focus session runs the guard settings are view-only and a banner says until when
 * ([guardsLock]); each section locks itself wherever it is shown. A search result opens its view, scrolled to its row.
 */
@Composable
fun GuardsPage(
    anchor: String?,
    onBack: () -> Unit,
    state: CompanionState,
    clock: ClockFormat,
    pause: PauseState?,
    pauseActions: PauseActions,
    guardsLock: GuardsLock?,
    pcName: String?,
    pcState: PcState?,
    deviceName: String,
    curbox: CurboxStatus,
    onConnectCurbox: () -> Unit,
    onReenableTime: (Boolean, java.time.LocalTime) -> String?,
    onAllowUsage: () -> Unit,
) {
    var viewName by rememberSaveable(anchor) { mutableStateOf(viewFor(anchor).name) }
    val view = GuardsView.valueOf(viewName)
    // Opening Essentials from the Advanced fold's LockMyPhone row lands on its card.
    var toLock by rememberSaveable(anchor) { mutableStateOf(anchor == SettingsAnchors.LOCKMYPHONE) }
    fun open(next: GuardsView) { viewName = next.name }
    BackHandler(enabled = view.parent != null) { open(view.parent ?: GuardsView.List) }
    val back = { view.parent?.let { open(it) } ?: onBack() }

    SubpageFrame(view.crumb, view.title, back) {
        when (view) {
            GuardsView.List -> GuardsList(anchor, state, clock, pause, guardsLock, pcName, pcState, deviceName, curbox) { open(GuardsView.PhoneGuard) }
            GuardsView.PhoneGuard -> PhoneGuardPage(
                anchor, clock, pause, pauseActions, guardsLock,
                onOpenEssentials = { toLock = false; open(GuardsView.Essentials) },
                onOpenLock = { toLock = true; open(GuardsView.Essentials) },
                onOpenCurbox = { open(GuardsView.CurboxSetup) },
            )
            GuardsView.Essentials -> Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                val context = LocalContext.current
                NeedsLine(listOf(Need("Usage access", remember { PhoneUsageReader(context).hasAccess() }, onAllowUsage)), forWhat = "Essentials")
                EssentialsSection(scrollToLock = toLock)
            }
            GuardsView.CurboxSetup -> Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp)) {
                CurboxSetupPage(onConnectCurbox, onReenableTime)
            }
        }
    }
}

/** The list: the Phone guard row, then the profiles by device (this phone first, then the PC). */
@Composable
private fun GuardsList(
    anchor: String?,
    state: CompanionState,
    clock: ClockFormat,
    pause: PauseState?,
    guardsLock: GuardsLock?,
    pcName: String?,
    pcState: PcState?,
    deviceName: String,
    curbox: CurboxStatus,
    onOpenPhoneGuard: () -> Unit,
) {
    val context = LocalContext.current
    var phoneExpanded by remember { mutableStateOf(true) }
    var pcExpanded by remember { mutableStateOf(true) }
    val guards = remember(curbox) { PhoneGuards.read(context, curbox) }
    val profiles = state.document.profiles
    val zone = ZoneId.systemDefault()
    val now = Instant.now()
    // "On", "Turning off" or "Off", the phone guard's master switch.
    val master = pause?.let {
        when (PauseRules.status(PauseRules.advance(it, now, zone, sessionRunning = guardsLock != null), now, zone)) {
            PauseStatus.On -> "On"
            is PauseStatus.Waiting -> "Turning off"
            is PauseStatus.Paused -> "Off"
        }
    }

    val rows = buildList {
        guardsLock?.let { lock -> add(PageRow("rest") { GuardsRestBanner(lock) }) }
        add(PageRow(SettingsAnchors.PHONE_GUARD) {
            AbCard(onClick = onOpenPhoneGuard, contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
                AbListRow(
                    "Phone guard",
                    icon = SectionIcons.Guards,
                    subtitle = "What your phone does while your PC session runs",
                    trailing = master?.let { word -> { Text(word, style = Ab.type.small, color = Ab.colors.muted) } },
                )
            }
        })
        add(PageRow("profiles-label") { AbSectionLabel("Profiles", Modifier.padding(start = 4.dp, top = 6.dp)) })
        // This phone first: what actually guards it.
        add(PageRow("this-phone") {
            ProfileDeviceRow(
                title = "$deviceName (this phone)",
                icon = Icons.Outlined.Smartphone,
                detail = PhoneGuards.summary(guards),
                expanded = phoneExpanded,
                onToggle = { phoneExpanded = !phoneExpanded },
            )
        })
        if (phoneExpanded) add(PageRow("this-phone-guards") { PhoneGuardCards(guards, pcName) })

        when {
            profiles.isEmpty() -> add(PageRow(SettingsAnchors.PROFILES) {
                Note("No PC profiles yet. Sign in to Google Drive in Settings › Devices & sync, or import a backup from the PC in Settings › App & data.")
            })
            pcName == null -> {
                add(PageRow(SettingsAnchors.PROFILES) {
                    Note("${state.importedFrom ?: "From the PC"}; imported ${state.importedAt ?: ""}. Edit profiles on the PC and import again.")
                })
                profiles.forEach { profile -> add(PageRow("profile-${profile.id}") { ProfileCard(profile) }) }
            }
            else -> {
                val running = when {
                    pcState == null -> "no session data yet"
                    pcState.isScheduledBlock -> "blocks on: ${pcState.profileName ?: "a profile"}"
                    pcState.isActive -> "running: ${pcState.profileName ?: "a profile"}"
                    else -> "nothing running"
                }
                add(PageRow(SettingsAnchors.PROFILES) {
                    ProfileDeviceRow(
                        title = pcName,
                        icon = Icons.Outlined.Computer,
                        detail = "${profiles.size} profile${if (profiles.size == 1) "" else "s"} · $running · synced ${state.importedAt ?: "not yet"}",
                        expanded = pcExpanded,
                        onToggle = { pcExpanded = !pcExpanded },
                    )
                })
                if (pcExpanded) profiles.forEach { profile -> add(PageRow("profile-${profile.id}") { ProfileCard(profile) }) }
            }
        }
    }
    SettingsPageList(rows, anchor)
}

/** The Phone guard page (board 5i). */
@Composable
private fun PhoneGuardPage(
    anchor: String?,
    clock: ClockFormat,
    pause: PauseState?,
    pauseActions: PauseActions,
    guardsLock: GuardsLock?,
    onOpenEssentials: () -> Unit,
    onOpenLock: () -> Unit,
    onOpenCurbox: () -> Unit,
) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val essentials by app.essentials.essentials.collectAsState()
    val rows = buildList {
        // While a focus session runs these are view-only (the guard lock); the banner says until when.
        guardsLock?.let { lock -> add(PageRow("rest") { GuardsRestBanner(lock) }) }
        if (pause != null) {
            add(PageRow(SettingsAnchors.PHONE_GUARD) {
                // Ticks the wait down while it's shown.
                // 1 s while the wait runs, 15 s while paused (when it ends), and not at all when the guard is simply on.
                val now by rememberTickingNow(pause) { if (pause.requestedAt != null) 1_000L else if (pause.pausedAt != null) 15_000L else null }
                PauseSection(pause, now, clock, pauseActions)
            })
        }
        add(PageRow(SettingsAnchors.PHONE_DOWN) { PhoneDownSection() })
        add(PageRow(SettingsAnchors.ESSENTIALS) {
            AbCard(onClick = onOpenEssentials, contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
                AbListRow(
                    "Essentials",
                    icon = Icons.Outlined.Apps,
                    subtitle = "${essentials.apps.size} app${if (essentials.apps.size == 1) "" else "s"} · ${essentials.folders.size} folder${if (essentials.folders.size == 1) "" else "s"}",
                )
            }
        })
        add(PageRow(SettingsAnchors.PLACEMENT) { PlacementSection() })
        add(PageRow(SettingsAnchors.EMERGENCY) { EmergencySection() })
        add(PageRow(SettingsAnchors.ADVANCED) {
            Section("Advanced", summary = "Car pause · takeover screen · nudge repeat · Curbox and LockMyPhone", startOpen = anchor == SettingsAnchors.ADVANCED) {
                if (pause != null) PauseAdvancedControls(pause, pauseActions)
                AbListRow(
                    "Curbox setup",
                    icon = Icons.Outlined.PhoneAndroid,
                    subtitle = "What to make in Curbox, blocks back on, hard mode",
                    onClick = onOpenCurbox,
                )
                AbListRow(
                    "LockMyPhone-Extended",
                    icon = Icons.Outlined.Lock,
                    subtitle = "The lock a session starts: its profile and lunch",
                    onClick = onOpenLock,
                )
            }
        })
    }
    SettingsPageList(rows, anchor)
}
