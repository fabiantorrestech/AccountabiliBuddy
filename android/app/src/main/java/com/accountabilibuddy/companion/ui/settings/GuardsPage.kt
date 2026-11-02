package com.accountabilibuddy.companion.ui.settings

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.curbox.CurboxStatus
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.pause.PauseState
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.companion.ui.EmergencySection
import com.accountabilibuddy.companion.ui.EssentialsSection
import com.accountabilibuddy.companion.ui.GuardsRestBanner
import com.accountabilibuddy.companion.ui.Need
import com.accountabilibuddy.companion.ui.NeedsLine
import com.accountabilibuddy.companion.ui.PauseActions
import com.accountabilibuddy.companion.ui.PauseSection
import com.accountabilibuddy.companion.ui.PlacementSection
import com.accountabilibuddy.companion.ui.ProfilesScreen
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import com.accountabilibuddy.companion.sync.PcState
import java.time.Instant

/**
 * Settings › Guards: the phone's own guard switches above the PC's profiles (read only). While a focus session runs the guard
 * settings are view-only and a banner says until when ([guardsLock]).
 */
@Composable
fun GuardsPage(
    anchor: String?,
    state: CompanionState,
    clock: ClockFormat,
    pause: PauseState?,
    pauseActions: PauseActions,
    guardsLock: GuardsLock?,
    pcName: String?,
    pcState: PcState?,
    deviceName: String,
    curbox: CurboxStatus,
    onAllowUsage: () -> Unit,
) {
    val context = LocalContext.current
    val usageNeed = Need("Usage access", remember { PhoneUsageReader(context).hasAccess() }, onAllowUsage)
    val listState = rememberLazyListState()
    ProfilesScreen(
        state,
        clock,
        pcName = pcName,
        pcState = pcState,
        deviceName = deviceName,
        curbox = curbox,
        listState = listState,
        header = { phoneGuardItems(pause, pauseActions, clock, usageNeed, guardsLock) },
    )
    ScrollToAnchor(listState, anchor, guardKeys(pause, guardsLock))
}

// The keys of the rows phoneGuardItems writes, in order, then the PC profiles' first row: where a search result scrolls to.
private fun guardKeys(pause: PauseState?, guardsLock: GuardsLock?): List<String> = buildList {
    if (guardsLock != null) add("rest")
    if (pause != null) add(SettingsAnchors.PHONE_GUARD)
    add(SettingsAnchors.ESSENTIALS)
    add(SettingsAnchors.EMERGENCY)
    add(SettingsAnchors.PLACEMENT)
    // ProfilesScreen then draws this phone's row and its cards (open at first), and only then the PC's profiles.
    add("this-phone")
    add("this-phone-guards")
    add(SettingsAnchors.PROFILES)
}

/**
 * Guards, the phone's half: the master switch with its wait, the work-start nudge and takeover, Essentials, emergency phone
 * time and the parked-phone placement. The Guards page puts these above the profiles.
 */
internal fun LazyListScope.phoneGuardItems(
    pause: PauseState?,
    pauseActions: PauseActions,
    clock: ClockFormat,
    usageNeed: Need,
    guardsLock: GuardsLock?,
) {
    // While a focus session runs these are view-only (the guard lock); the banner says until when.
    guardsLock?.let { lock -> item(key = "rest") { GuardsRestBanner(lock) } }
    if (pause != null) {
        item(key = SettingsAnchors.PHONE_GUARD) {
            // Ticks the wait down while it's shown.
            var now by remember { mutableStateOf(Instant.now()) }
            LaunchedEffect(pause) {
                while (true) {
                    now = Instant.now()
                    kotlinx.coroutines.delay(1_000)
                }
            }
            PauseSection(pause, now, clock, pauseActions)
        }
    }
    item(key = SettingsAnchors.ESSENTIALS) {
        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            NeedsLine(listOf(usageNeed), forWhat = "Essentials")
            EssentialsSection()
        }
    }
    item(key = SettingsAnchors.EMERGENCY) { EmergencySection() }
    item(key = SettingsAnchors.PLACEMENT) { PlacementSection() }
}
