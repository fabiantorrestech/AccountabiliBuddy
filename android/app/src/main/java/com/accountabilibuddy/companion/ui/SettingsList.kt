package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.curbox.CurboxStatus
import com.accountabilibuddy.companion.data.CompanionState
import com.accountabilibuddy.companion.lunch.LunchStore
import com.accountabilibuddy.companion.pause.PauseState
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.companion.ui.kit.AbListRow
import com.accountabilibuddy.companion.ui.kit.AbPageHeader

/** What the list's one-line summaries are worked out from. */
data class SettingsListInputs(
    val state: CompanionState,
    val health: HealthStatus,
    val lunchLengthMinutes: Int,
    val pause: PauseState?,
    val takeoverOn: Boolean,
    val curbox: CurboxStatus,
    /** "In your device group · Drive": the Devices & sync row's status. */
    val devicesSummary: String,
    val appVersion: String,
    /** A focus session is running: the guard settings are view-only. Guards shows its lock and a banner says until when. */
    val guardsLock: GuardsLock?,
)

/**
 * Settings (boards 5e, 6b): the title, an optional "resting" line and the pages as rows with a one-line status. Eight pages in
 * the first card, then App & data, Setup and About below a gap. A page opens through [onOpen]; the list knows nothing of the
 * page's content, so a wide layout can show list and page side by side.
 */
@Composable
fun SettingsList(inputs: SettingsListInputs, onOpen: (SettingsCategory) -> Unit, listState: LazyListState = rememberLazyListState()) {
    val context = LocalContext.current
    val theme by UiPrefs.themeName.collectAsState()
    val mode by UiPrefs.mode.collectAsState()
    val summaries = settingsSummaries(inputs, "${theme.label} · ${mode.label.lowercase()}", permissionsSummary(context, inputs.health))

    LazyColumn(state = listState, contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        item { AbPageHeader("Settings") }
        inputs.guardsLock?.let { lock ->
            item { GuardsRestBanner(lock) }
        }
        item { CategoryCard(SettingsCategory.Main, summaries, inputs.guardsLock != null, onOpen) }
        item { CategoryCard(SettingsCategory.Below, summaries, inputs.guardsLock != null, onOpen) }
        item {
            Hint(
                "Local only: the app talks only to your linked watch (through Google's encrypted relay only if you allow it in Devices & sync) " +
                    "and, with Google Drive on, to the app's own Drive folder, where your devices' sealed sync files go. Nothing else goes to the internet.",
                label = "Privacy: what leaves this phone",
            )
        }
    }
}

@Composable
private fun CategoryCard(
    categories: List<SettingsCategory>,
    summaries: Map<SettingsCategory, String>,
    resting: Boolean,
    onOpen: (SettingsCategory) -> Unit,
) {
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
        categories.forEachIndexed { index, category ->
            AbListRow(
                title = category.title, icon = category.icon, subtitle = summaries[category]?.takeIf { it.isNotEmpty() },
                onClick = { onOpen(category) }, locked = resting && category == SettingsCategory.Guards,
                divider = index < categories.lastIndex,
            )
        }
    }
}

private fun settingsSummaries(inputs: SettingsListInputs, appearance: String, permissions: String): Map<SettingsCategory, String> {
    val sleep = inputs.state.sleep
    return mapOf(
        SettingsCategory.YourDay to "Lunch ${LunchStore.formatLength(inputs.lunchLengthMinutes)} · work hours and day types in Plan",
        SettingsCategory.Sleep to listOfNotNull(
            if (sleep.enabled) "Sleep mode on" else "Sleep mode off",
            "smart bedtime".takeIf { sleep.smartWorkdays || sleep.smartOffDays },
            "reminders".takeIf { sleep.reminders.on },
        ).joinToString(" · "),
        SettingsCategory.FocusSessions to "Test session",
        SettingsCategory.Guards to listOfNotNull(
            inputs.pause?.let { if (it.pausedAt != null) "Paused" else "Guarding" },
            "work card".takeIf { inputs.takeoverOn },
            "${inputs.state.document.profiles.size} profiles".takeIf { inputs.state.document.profiles.isNotEmpty() },
        ).joinToString(" · "),
        SettingsCategory.Alerts to (if (inputs.state.timelineNotificationOn) "Timeline pinned" else "Timeline off") + " · noise player · headphones",
        SettingsCategory.Devices to inputs.devicesSummary,
        SettingsCategory.Connections to listOfNotNull("Beeminder", "Curbox".takeIf { inputs.curbox.granted }, "other blockers").joinToString(", "),
        SettingsCategory.Appearance to appearance,
        SettingsCategory.AppAndData to "Startup · backup · import",
        SettingsCategory.Setup to permissions,
        SettingsCategory.About to "Version ${inputs.appVersion}",
    )
}
