package com.accountabilibuddy.companion.debug

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material.icons.outlined.Tune
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.DeviceKind
import com.accountabilibuddy.companion.ui.SectionIcons
import com.accountabilibuddy.companion.ui.SyncDot
import com.accountabilibuddy.companion.ui.SyncLight
import com.accountabilibuddy.companion.ui.SyncLightDevice
import com.accountabilibuddy.companion.ui.SyncLightModel
import com.accountabilibuddy.companion.ui.ThemedContent
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbInfoBanner
import com.accountabilibuddy.companion.ui.kit.AbInsetRow
import com.accountabilibuddy.companion.ui.kit.AbListRow
import com.accountabilibuddy.companion.ui.kit.AbPageHeader
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.kit.AbRing
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.kit.AbSubpageHeader
import com.accountabilibuddy.companion.ui.kit.AbTimerText
import com.accountabilibuddy.companion.ui.kit.AbToggleRow
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.companion.ui.theme.ThemeName

/**
 * Debug only: every item of the component kit in one scrolling page, for screenshots on the emulator. Opened with
 * `adb shell am start -n com.accountabilibuddy.companion/.debug.KitGalleryActivity --es theme paper --ez dark true
 * [--ez quiet false] [--es show sheet|dialog]`. It draws one theme and one variant whatever the app's own setting is.
 */
class KitGalleryActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        val theme = when (intent.getStringExtra("theme")?.lowercase()) {
            "paper" -> ThemeName.Paper
            "nightdesk", "night" -> ThemeName.NightDesk
            else -> ThemeName.Instrument
        }
        val dark = intent.getBooleanExtra("dark", true)
        val quiet = intent.getBooleanExtra("quiet", true)
        val show = intent.getStringExtra("show")
        setContent {
            ThemedContent(theme, dark, quiet, 1f) { when (show) {
                "plan" -> PlanGallery(intent)
                "stats" -> StatsGallery(intent)
                else -> Gallery(show)
            } }
        }
    }
}

@Composable
private fun Gallery(show: String?) {
    if (show == "sync") {
        SyncLightStates()
        return
    }
    var segmented by remember { mutableStateOf(1) }
    var chip by remember { mutableStateOf(0) }
    var toggle by remember { mutableStateOf(true) }
    var sheet by remember { mutableStateOf(show == "sheet") }
    var dialog by remember { mutableStateOf(show == "dialog") }
    Column(
        Modifier.fillMaxSize().background(Ab.colors.bg).safeDrawingPadding().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        AbPageHeader("Kit gallery", actions = {
            AbQuietIconButton(Icons.Outlined.Search, "Search", {})
            AbQuietIconButton(Icons.Outlined.Tune, "Tune", {})
        })
        AbSubpageHeader(crumb = "Settings", title = "Subpage title", onBack = {})

        AbCard {
            AbSectionLabel("AbSectionLabel and AbTimerText")
            AbTimerText("18:42")
            Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                AbRing(0.35f)
                AbRing(0.8f, diameter = 40.dp, strokeWidth = 5.dp)
                AbTimerText("1h 10m", style = Ab.type.timerSmall)
            }
        }
        AbCard {
            AbSectionLabel("AbSegmented")
            AbSegmented(listOf("Light", "Dark", "System"), segmented, { segmented = it })
            AbSegmented(listOf("At a set time", "After sunset"), null, {})
        }
        AbCard {
            AbSectionLabel("AbChipRow (scrolls sideways)")
            AbChipRow {
                listOf("All", "Work", "Home", "Errands", "Reading", "Admin", "Someday").forEachIndexed { i, label -> AbChip(label, i == chip, { chip = i }) }
            }
        }
        AbCard {
            AbSectionLabel("AbListRow")
            AbListRow("Your day", icon = SectionIcons.YourDay, subtitle = "Work hours, lunch, day types", onClick = {}, divider = true)
            AbListRow("Guards", icon = SectionIcons.Guards, subtitle = "PC drift, phone guard", locked = true, onClick = {}, divider = true)
            AbListRow("Appearance", icon = SectionIcons.Appearance, subtitle = "Night Desk, calm, dark", onClick = {})
        }
        AbCard {
            AbSectionLabel("AbInsetRow and AbToggleRow")
            AbInsetRow { Text("Pull last quarter's numbers", color = Ab.colors.ink) }
            AbInsetRow(onClick = {}) { Text("Tappable inset row", color = Ab.colors.ink) }
            AbToggleRow("Timeline notification", checked = toggle, onCheckedChange = { toggle = it }, subtitle = "Always on, silent, pinned near the top")
            AbToggleRow("Disabled toggle", checked = false, onCheckedChange = {}, enabled = false)
        }
        AbInfoBanner(buildAnnotatedString {
            append("Guards rest while you focus, back at ")
            withStyle(SpanStyle(fontWeight = FontWeight.Bold)) { append("2:20 PM") }
            append(".")
        })
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            AbTonalButton("Pause", {}, Modifier.weight(1f))
            AbPrimaryButton("Done", {}, Modifier.weight(1f))
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            AbTonalButton("Open sheet", { sheet = true }, Modifier.weight(1f))
            AbTonalButton("Open dialog", { dialog = true }, Modifier.weight(1f))
        }
        AbCard(onClick = {}) { Text("A clickable AbCard", color = Ab.colors.ink) }
    }

    if (sheet) {
        AbSheet(onDismiss = { sheet = false }) {
            Text("AbSheet", style = Ab.type.section, color = Ab.colors.ink)
            Text("A Material 3 modal bottom sheet in the theme's surface.", style = Ab.type.small, color = Ab.colors.muted)
            AbPrimaryButton("Close", { sheet = false }, Modifier.fillMaxWidth())
        }
    }
    if (dialog) {
        AbDialog(title = "AbDialog", onDismiss = { dialog = false }, confirmText = "Ok", onConfirm = { dialog = false }) {
            Text("A dialog in the theme's surface.", color = Ab.colors.ink)
        }
    }
}

/** Board 20a's five states of the sync light, each from a model (no real sync state needed), for screenshots. `--es show sync`. */
@Composable
private fun SyncLightStates() {
    fun devices(pcDim: Boolean = false, phoneDim: Boolean = false, watchDim: Boolean = false) = listOf(
        SyncLightDevice("Work PC", DeviceKind.PC, pcDim, "1 min ago"),
        SyncLightDevice("This phone", DeviceKind.PHONE, false, "now"),
        SyncLightDevice("Other phone", DeviceKind.PHONE, phoneDim, if (phoneDim) "3 h ago" else "2 min ago"),
        SyncLightDevice("Pixel Watch", DeviceKind.WATCH, watchDim, if (watchDim) "not reachable" else "connected"),
    )
    val states = listOf(
        "All in sync" to SyncLightModel(SyncDot.InSync, devices(), "Synced 1 min ago · Google Drive"),
        "One behind" to SyncLightModel(SyncDot.InSync, devices(phoneDim = true), "Synced 1 min ago · Google Drive"),
        "Syncing now" to SyncLightModel(SyncDot.Syncing, devices(), "Synced 1 min ago · Google Drive"),
        "Several behind" to SyncLightModel(SyncDot.SeveralBehind, devices(phoneDim = true, watchDim = true), "Synced 3 h ago · Google Drive"),
        "This device offline" to SyncLightModel(SyncDot.Offline, devices(pcDim = true, phoneDim = true, watchDim = true), "This phone can't sync: Google Drive: no connection"),
    )
    Column(
        Modifier.fillMaxSize().background(Ab.colors.bg).safeDrawingPadding().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        AbPageHeader("Sync light")
        states.forEach { (name, model) ->
            AbCard {
                AbSectionLabel(name)
                SyncLight(model, refreshing = model.dot == SyncDot.Syncing, onSyncNow = {}, onOpenDevices = {})
            }
        }
    }
}
