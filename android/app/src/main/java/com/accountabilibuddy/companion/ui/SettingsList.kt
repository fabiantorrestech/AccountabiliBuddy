package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.ui.Modifier
import com.accountabilibuddy.companion.ui.kit.AbSearchField
import com.accountabilibuddy.companion.ui.settings.SearchEntry
import com.accountabilibuddy.companion.ui.settings.SettingsSearch
import com.accountabilibuddy.companion.ui.theme.Ab
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
 * Settings (boards 5e, 6b): the title, a search field, an optional "resting" line and the pages as rows with a one-line status.
 * Eight pages in the first card, then App & data, Setup and About below a gap. A page opens through [onOpen]; the list knows nothing
 * of the page's content, so a wide layout can show list and page side by side.
 *
 * Typing in the search field ([query], kept by the caller in memory only) swaps the pages for what [SettingsSearch] finds;
 * a result opens its page at its row through [onOpenResult].
 */
@Composable
fun SettingsList(
    inputs: SettingsListInputs,
    onOpen: (SettingsCategory) -> Unit,
    query: String = "",
    onQueryChange: (String) -> Unit = {},
    onOpenResult: (SearchEntry) -> Unit = {},
    listState: LazyListState = rememberLazyListState(),
    /** The page showing beside the list on a tablet; its row is marked. Null on a phone. */
    selected: SettingsCategory? = null,
) {
    val context = LocalContext.current
    val theme by UiPrefs.themeName.collectAsState()
    val mode by UiPrefs.mode.collectAsState()
    val summaries = settingsSummaries(inputs, "${theme.label} · ${mode.label.lowercase()}", permissionsSummary(context, inputs.health))

    val searching = query.isNotBlank()
    val results = remember(query) { SettingsSearch.search(query) }

    LazyColumn(state = listState, contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 0.dp, bottom = 16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        // One item, 12 dp apart: the first row under the title sits where every tab's first row sits (brief §3.1 rule 1).
        item {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                AbPageHeader("Settings")
                AbSearchField(query, onQueryChange, "Search settings")
            }
        }
        inputs.guardsLock?.let { lock ->
            item { GuardsRestBanner(lock) }
        }
        if (searching) {
            item { SearchResults(results, inputs.guardsLock != null, onOpenResult) }
        } else {
            item { CategoryCard(SettingsCategory.Main, summaries, inputs.guardsLock != null, onOpen, selected) }
            item { CategoryCard(SettingsCategory.Below, summaries, inputs.guardsLock != null, onOpen, selected) }
            item {
                Hint(
                    "Local only: the app talks only to your linked watch (through Google's encrypted relay only if you allow it in Devices & sync) " +
                        "and, with Google Drive on, to the app's own Drive folder, where your devices' sealed sync files go. Nothing else goes to the internet.",
                    label = "Privacy: what leaves this phone",
                )
            }
        }
    }
}

/** What the search found: a row each with the page it is on, or a line saying nothing matched. */
@Composable
private fun SearchResults(results: List<SearchEntry>, resting: Boolean, onOpenResult: (SearchEntry) -> Unit) {
    if (results.isEmpty()) {
        Text("Nothing in Settings matches that.", style = Ab.type.bodyMedium, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 4.dp))
        return
    }

    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
        results.forEachIndexed { index, entry ->
            AbListRow(
                title = entry.title, icon = entry.category.icon, subtitle = "Settings › ${entry.category.title}",
                onClick = { onOpenResult(entry) }, locked = resting && (entry.category == SettingsCategory.Guards || entry.category == SettingsCategory.Goals), divider = index < results.lastIndex,
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
    selected: SettingsCategory? = null,
) {
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
        categories.forEachIndexed { index, category ->
            AbListRow(
                title = category.title, icon = category.icon, subtitle = summaries[category]?.takeIf { it.isNotEmpty() },
                onClick = { onOpen(category) }, locked = resting && (category == SettingsCategory.Guards || category == SettingsCategory.Goals),
                divider = index < categories.lastIndex,
                modifier = if (category == selected) Modifier.background(Ab.colors.surface2, Ab.shapes.mediumShape) else Modifier,
            )
        }
    }
}

private fun settingsSummaries(inputs: SettingsListInputs, appearance: String, permissions: String): Map<SettingsCategory, String> {
    val sleep = inputs.state.sleep
    return mapOf(
        SettingsCategory.YourDay to "Lunch ${LunchStore.formatLength(inputs.lunchLengthMinutes)} · work hours and day types in Plan",
        SettingsCategory.Goals to "Weekly review · default reminders · areas",
        SettingsCategory.Sleep to listOfNotNull(
            if (sleep.enabled) "Sleep mode on" else "Sleep mode off",
            "smart bedtime".takeIf { sleep.smartWorkdays || sleep.smartOffDays },
            "reminders".takeIf { sleep.reminders.on },
        ).joinToString(" · "),
        SettingsCategory.FocusSessions to "Test session",
        SettingsCategory.Tasks to "Labels · reminders · during a session",
        SettingsCategory.Guards to listOfNotNull(
            inputs.pause?.let { if (it.pausedAt != null) "Paused" else "Guarding" },
            "work card".takeIf { inputs.takeoverOn },
            "${inputs.state.document.profiles.size} profiles".takeIf { inputs.state.document.profiles.isNotEmpty() },
        ).joinToString(" · "),
        SettingsCategory.Alerts to (if (inputs.state.timelineNotificationOn) "Timeline pinned" else "Timeline off") + " · noise player · headphones",
        SettingsCategory.Devices to inputs.devicesSummary,
        SettingsCategory.Connections to listOfNotNull("Beeminder", "Curbox".takeIf { inputs.curbox.granted }, "other blockers").joinToString(", "),
        SettingsCategory.Appearance to appearance,
        SettingsCategory.AppAndData to "App mode · startup · backup",
        SettingsCategory.Setup to permissions,
        SettingsCategory.About to "Version ${inputs.appVersion}",
    )
}
