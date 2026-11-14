package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.Label
import androidx.compose.material.icons.automirrored.outlined.Sort
import androidx.compose.material.icons.outlined.BookmarkAdd
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.Event
import androidx.compose.material.icons.outlined.GridView
import androidx.compose.material.icons.outlined.History
import androidx.compose.material.icons.outlined.HourglassEmpty
import androidx.compose.material.icons.outlined.PushPin
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material.icons.outlined.Shuffle
import androidx.compose.material.icons.outlined.Star
import androidx.compose.material.icons.outlined.SwapHoriz
import androidx.compose.material.icons.outlined.Tag
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material.icons.outlined.ViewHeadline
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.tasks.TasksUiState
import com.accountabilibuddy.companion.ui.kit.AbOutlinedButton
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.kit.AbSegmentedCompact
import com.accountabilibuddy.companion.ui.kit.AbSwitch
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetCheck
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.sheets.AbSheetRow
import com.accountabilibuddy.companion.ui.sheets.AbSheetValue
import com.accountabilibuddy.companion.ui.tasks.SheetField
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.TaskLabels
import com.accountabilibuddy.shared.tasks.ShelfPages
import com.accountabilibuddy.shared.tasks.ShelfPaging
import com.accountabilibuddy.shared.tasks.TaskHistoryKind
import com.accountabilibuddy.shared.tasks.TaskHistoryRules
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskRowText
import com.accountabilibuddy.shared.tasks.TaskView
import com.accountabilibuddy.shared.tasks.TaskViewConfig
import com.accountabilibuddy.shared.tasks.TaskViewIcons
import com.accountabilibuddy.shared.tasks.TaskViewRules
import java.time.OffsetDateTime
import java.time.ZoneId
import kotlin.random.Random

/**
 * The Tasks Options sheet (board Tasks-Phone-Options, brief §4.5), 80% tall: "Options · View, filter and sort". The saved view chips
 * first (★ Default, Quick wins, Deep work, + Save); **ARRANGE** first on purpose, so the paging settings are easy to find (Group by,
* a pill segment Bins · Label · Tag · None in the sheet · Page order · Sort · Rows Normal / Compact · Pin highlights to the top); **SHOW ONLY** (Due date · Labels · Tags · Time
 * limit · Untouched for · Highlighted only); **MORE** (History · Tasks settings ›, always the last row). The sticky footer on this
 * sheet and every sub-page: Clear · Save (faded until something changed; tapping it then says what to do) · **Show 10 tasks**, a live
 * count.
 *
 * A change applies at once to the view in use ([state], kept on this device through [onState]); the rows with › open a sub-page of
 * the same sheet with a back arrow ([OptionsPage]). [start] opens one directly (the header's Views icon opens Saved views).
 */
@Composable
fun TasksOptionsSheet(
    state: TasksUiState,
    onState: ((TasksUiState) -> TasksUiState) -> Unit,
    lists: TaskLists,
    now: OffsetDateTime,
    zone: ZoneId,
    controller: ShelfController,
    onOpenSettings: () -> Unit,
    onDismiss: () -> Unit,
    start: OptionsPage = OptionsPage.Main,
) {
    val colors = Ab.colors
    var page by remember { mutableStateOf(start) }
    var explain by remember { mutableStateOf(false) }
    // The saved view whose icon the View icon page is changing.
    var iconOf by remember { mutableStateOf<String?>(null) }
    // The grouping the Page order page is showing (its Bins | Labels | Tags switch); null follows the view's own grouping.
    var orderGrouping by remember { mutableStateOf<String?>(null) }
    val config = state.config
    val setConfig: ((TaskViewConfig) -> TaskViewConfig) -> Unit = { change -> explain = false; onState { it.copy(config = change(it.config)) } }
    val views = lists.views.map { it.view }.filter { it.isLive }.sortedBy { it.position }.ifEmpty { TaskView.builtIns(now) }
    val active = views.firstOrNull { it.id.equals(state.viewId ?: TaskView.DEFAULT_ID, ignoreCase = true) }
    val changed = active == null || active.config != config
    // ADR 89: `lists` is already the list as this phone shows it; the pickers group it ("Shared", "This device") and a view naming a hidden label or tag says whose.
    val app = androidx.compose.ui.platform.LocalContext.current.applicationContext as com.accountabilibuddy.companion.CompanionApp
    val scopeView = com.accountabilibuddy.companion.ui.tasks.rememberScopeView(app)
    val viewNote = scopeView.viewNote(config.labels, config.tags) { com.accountabilibuddy.companion.ui.tasks.scopeDeviceName(app, it) }
    val shown = ShelfPages.count(ShelfPages.build(lists, config, now, zone, storedOrder = state.orderOf(state.grouping)))
    val applyView: (TaskView) -> Unit = { view -> explain = false; onState { it.copy(config = view.config, viewId = view.id) } }
    val back: (() -> Unit)? = when {
        page == start && start != OptionsPage.Main -> onDismiss
        page == OptionsPage.Main -> null
        page == OptionsPage.CreateView && start == OptionsPage.Views -> ({ page = OptionsPage.Views })
        page == OptionsPage.ViewIcon -> ({ page = OptionsPage.Views })
        else -> ({ page = OptionsPage.Main })
    }

    AbSheet(
        title = page.title,
        subtitle = page.subtitle,
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        onBack = back,
        footer = {
            if (page == OptionsPage.PageOrder) {
                val grouping = orderGrouping ?: state.grouping ?: TaskViewConfig.GROUP_BINS
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    AbOutlinedButton("Reset", onClick = { onState { it.copy(orders = it.orders - grouping, starts = it.starts - grouping, wraps = it.wraps - grouping) } })
                    AbPrimaryButton("Done", onClick = { page = OptionsPage.Main }, modifier = Modifier.weight(1f))
                }
            } else if (page != OptionsPage.CreateView) {
                if (explain) Text("Change a filter, grouping or sort first", style = Ab.type.small, color = colors.muted)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    AbOutlinedButton("Clear", onClick = { setConfig { TaskViewWords.cleared(it) } })
                    AbOutlinedButton(
                        "Save", onClick = { if (changed) page = OptionsPage.CreateView else explain = true }, icon = Icons.Outlined.BookmarkAdd,
                        modifier = Modifier.alpha(if (changed) 1f else 0.45f),
                    )
                    AbPrimaryButton(ShelfPages.showText(shown), onClick = onDismiss, modifier = Modifier.weight(1f))
                }
            }
        },
    ) {
        when (page) {
            OptionsPage.Main -> {
                Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(start = 24.dp, end = 24.dp, bottom = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    views.forEach { view -> ShelfChip(TaskViewWords.chip(view), selected = view == active && !changed, height = 34) { applyView(view) } }
                    ShelfChip("+ Save", selected = false, height = 34) { if (changed) page = OptionsPage.CreateView else explain = true }
                }
                SheetSectionHead("Arrange")
                AbSheetRow("Group by", onClick = null, icon = Icons.Outlined.GridView, subtitle = "Each group is a page you swipe between")
                AbSegmented(
                    TaskViewWords.GROUP_PILLS.map { it.second }, TaskViewWords.groupPill(config.group),
                    onSelect = { index -> setConfig { it.copy(group = TaskViewWords.GROUP_PILLS[index].first) } },
                    modifier = Modifier.padding(horizontal = 24.dp, vertical = 2.dp), groupName = "Group by",
                )
                val grouping = state.grouping
                val pages = ShelfPages.build(lists, config, now, zone, storedOrder = state.orderOf(grouping))
                val startTitle = pages.getOrNull(ShelfPaging.startIndex(pages.map { it.key }, state.startOf(grouping)))?.title
                AbSheetRow(
                    "Page order", onClick = { orderGrouping = null; page = OptionsPage.PageOrder }, icon = Icons.Outlined.SwapHoriz, enabled = grouping != null,
                    subtitle = if (grouping == null) "One list has no pages" else ShelfPaging.summary(startTitle, state.wrapOf(grouping)),
                    trailing = { AbSheetValue(null) },
                )
                AbSheetRow("Sort", onClick = { page = OptionsPage.Sort }, icon = Icons.AutoMirrored.Outlined.Sort, trailing = { AbSheetValue(TaskViewWords.sort(config.sort)) })
                AbSheetRow("Rows", onClick = null, icon = Icons.Outlined.ViewHeadline, trailing = {
                    AbSegmentedCompact(
                        listOf("Normal", "Compact"), if (config.density == TaskViewConfig.DENSITY_COMPACT) 1 else 0,
                        onSelect = { index -> setConfig { it.copy(density = if (index == 1) TaskViewConfig.DENSITY_COMPACT else TaskViewConfig.DENSITY_NORMAL) } },
                    )
                })
                AbSheetRow(
                    "Pin highlights to the top", onClick = { setConfig { it.copy(pinHighlights = !it.pinHighlights) } }, icon = Icons.Outlined.PushPin, role = Role.Switch,
                    trailing = { AbSwitch(config.pinHighlights) },
                )
                SheetSectionHead("Show only")
                AbSheetRow("Due date", onClick = { page = OptionsPage.Due }, icon = Icons.Outlined.Event, trailing = { AbSheetValue(TaskViewWords.picked(config.only.due, TaskViewWords.DUE)) })
                AbSheetRow(
                    "Labels", onClick = { page = OptionsPage.Labels }, icon = Icons.AutoMirrored.Outlined.Label,
                    trailing = { AbSheetValue(TaskViewWords.pickedCount(scopeView.shownLabelCount(config.labels) + if (config.noLabel) 1 else 0)) },
                )
                AbSheetRow("Tags", onClick = { page = OptionsPage.Tags }, icon = Icons.Outlined.Tag, trailing = { AbSheetValue(TaskViewWords.pickedCount(scopeView.shownTagCount(config.tags))) })
                AbSheetRow("Time limit", onClick = { page = OptionsPage.Time }, icon = Icons.Outlined.Timer, trailing = { AbSheetValue(TaskViewWords.picked(config.only.time, TaskViewWords.TIME)) })
                AbSheetRow("Untouched for", onClick = { page = OptionsPage.Old }, icon = Icons.Outlined.HourglassEmpty, trailing = { AbSheetValue(TaskViewWords.picked(config.only.old, TaskViewWords.OLD)) })
                AbSheetRow(
                    "Highlighted only", onClick = { setConfig { it.copy(only = it.only.copy(highlightedOnly = !it.only.highlightedOnly)) } }, icon = Icons.Outlined.Star, role = Role.Switch,
                    trailing = { AbSwitch(config.only.highlightedOnly) },
                )
                SheetSectionHead("More")
                AbSheetRow("History", onClick = { page = OptionsPage.History }, icon = Icons.Outlined.History, subtitle = "Finished and deleted tasks", trailing = { AbSheetValue(null) })
                AbSheetRow(
                    "Tasks settings", onClick = { onDismiss(); onOpenSettings() }, icon = Icons.Outlined.Settings, subtitle = "Labels, reminders, during a session",
                    trailing = { AbSheetValue(null) },
                )
            }

            OptionsPage.Views -> {
                views.forEach { view ->
                    AbSheetRow(
                        TaskViewWords.chip(view), onClick = { applyView(view) }, role = Role.RadioButton, icon = ViewIconVectors.of(view.icon),
                        subtitle = if (view.builtIn) "Built in" else null,
                        trailing = {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                AbSheetCheck(view == active && !changed)
                                if (!view.builtIn) {
                                    IconButton(onClick = { iconOf = view.id; page = OptionsPage.ViewIcon }) {
                                        Icon(Icons.Outlined.Edit, contentDescription = "Change the icon of the view ${view.name}", tint = colors.muted)
                                    }
                                    IconButton(onClick = { controller.deleteView(view.id); if (state.viewId.equals(view.id, ignoreCase = true)) onState { it.copy(viewId = null) } }) {
                                        Icon(Icons.Outlined.Delete, contentDescription = "Delete the view ${view.name}", tint = colors.muted)
                                    }
                                }
                            }
                        },
                    )
                }
                AbSheetRow(
                    "Save this as a view", onClick = { if (changed) page = OptionsPage.CreateView else explain = true }, icon = Icons.Outlined.BookmarkAdd,
                    subtitle = if (changed) "The grouping, sort and filters as they are now" else "Change a filter, grouping or sort first",
                )
            }

            OptionsPage.CreateView -> {
                var name by remember { mutableStateOf("") }
                var icon by remember { mutableStateOf(TaskViewIcons.DEFAULT) }
                val taken = views.any { it.name.trim().equals(name.trim(), ignoreCase = true) }
                val save = {
                    val id = if (taken) null else controller.createView(name, config, icon)
                    if (id != null) {
                        onState { it.copy(viewId = id) }
                        page = if (start == OptionsPage.Views) OptionsPage.Views else OptionsPage.Main
                    }
                }
                Column(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    SheetField(value = name, onValueChange = { name = it.take(40) }, placeholder = "View name", onDone = save, singleLine = true)
                    if (taken) Text("You already have a view with that name.", style = Ab.type.small, color = colors.muted)
                    Text("Icon", style = Ab.type.small, color = colors.muted)
                    ViewIconPicker(icon, onPick = { icon = it })
                    AbPrimaryButton("Save view", onClick = save, enabled = name.isNotBlank() && !taken, modifier = Modifier.fillMaxWidth())
                }
            }

            OptionsPage.ViewIcon -> {
                val target = views.firstOrNull { it.id.equals(iconOf, ignoreCase = true) }
                Column(Modifier.padding(horizontal = 24.dp, vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    if (target != null) Text(target.name, style = Ab.type.small, color = colors.muted)
                    ViewIconPicker(target?.icon, onPick = { name -> target?.let { controller.setViewIcon(it.id, name) }; page = OptionsPage.Views })
                }
            }

            OptionsPage.PageOrder -> PageOrderPage(state, onState, lists, now, zone, orderGrouping ?: state.grouping ?: TaskViewConfig.GROUP_BINS, onGrouping = { orderGrouping = it })

            OptionsPage.Sort -> {
                TaskViewWords.SORTS.forEach { (value, name) ->
                    AbSheetRow(
                        name, role = Role.RadioButton, icon = if (value == TaskViewRules.SORT_SHUFFLE) Icons.Outlined.Shuffle else null,
                        onClick = {
                            setConfig { it.copy(sort = value, seed = if (value == TaskViewRules.SORT_SHUFFLE) (it.seed ?: Random.nextInt(1, Int.MAX_VALUE)) else it.seed) }
                            if (value != TaskViewRules.SORT_SHUFFLE) page = OptionsPage.Main
                        },
                        trailing = { AbSheetCheck(config.sort == value) },
                    )
                }
                if (config.sort == TaskViewRules.SORT_SHUFFLE) {
                    AbSheetRow("Reshuffle", onClick = { setConfig { it.copy(seed = Random.nextInt(1, Int.MAX_VALUE)) } }, icon = Icons.Outlined.Shuffle, subtitle = "The order stays until you reshuffle")
                }
            }

            OptionsPage.Due -> {
                PickRows(TaskViewWords.DUE, config.only.due) { value -> setConfig { it.copy(only = it.only.copy(due = TaskViewWords.toggled(it.only.due, value))) } }
                AbSheetRow("Clear this filter", onClick = { setConfig { it.copy(only = it.only.copy(due = null)) } }, enabled = !config.only.due.isNullOrEmpty())
                SheetFoot("Ticks in one filter widen it: any of them can match. Different filters narrow it: a task must match each one.")
            }

            OptionsPage.Time -> {
                PickRows(TaskViewWords.TIME, config.only.time) { value -> setConfig { it.copy(only = it.only.copy(time = TaskViewWords.toggled(it.only.time, value))) } }
                AbSheetRow("Clear this filter", onClick = { setConfig { it.copy(only = it.only.copy(time = null)) } }, enabled = !config.only.time.isNullOrEmpty())
            }

            OptionsPage.Old -> {
                PickRows(TaskViewWords.OLD, config.only.old) { value -> setConfig { it.copy(only = it.only.copy(old = TaskViewWords.toggled(it.only.old, value))) } }
                AbSheetRow("Clear this filter", onClick = { setConfig { it.copy(only = it.only.copy(old = null)) } }, enabled = !config.only.old.isNullOrEmpty())
            }

            OptionsPage.Labels -> {
                SheetMarkRow("No label", onClick = { setConfig { it.copy(noLabel = !it.noLabel) } }, leading = { LabelDot(null, 12.dp) }) { AbSheetCheck(config.noLabel) }
                LabelGroups(scopeView) { label ->
                    SheetMarkRow(label.name, onClick = { setConfig { it.copy(labels = TaskViewWords.toggled(it.labels, label.id)) } }, leading = { LabelDot(label.color, 12.dp) }) {
                        AbSheetCheck(config.labels.orEmpty().any { it.equals(label.id, ignoreCase = true) })
                    }
                }
                viewNote?.let { SheetFoot(it) }
                AbSheetRow("Clear this filter", onClick = { setConfig { it.copy(labels = null, noLabel = false) } }, enabled = !config.labels.isNullOrEmpty() || config.noLabel)
            }

            OptionsPage.Tags -> {
                val allTags = scopeView.knownTags()
                if (allTags.isEmpty()) SheetFoot("No task has a tag yet. Add one from a task's ⋮ menu.")
                scopeView.tagGroups(allTags).forEach { group ->
                    ScopeGroupHead(group)
                    group.items.forEach { tag ->
                        AbSheetRow("#$tag", onClick = { setConfig { it.copy(tags = TaskViewWords.toggled(it.tags, tag)) } }, icon = Icons.Outlined.Tag, role = Role.Checkbox, trailing = { AbSheetCheck(TaskLabels.has(config.tags, tag)) })
                    }
                }
                viewNote?.let { SheetFoot(it) }
                if (config.tags.orEmpty().size > 1) {
                    AbSheetRow("A task must have", onClick = null, trailing = {
                        AbSegmentedCompact(
                            listOf("Any", "All"), if (config.match == TaskViewConfig.MATCH_ALL) 1 else 0,
                            onSelect = { index -> setConfig { it.copy(match = if (index == 1) TaskViewConfig.MATCH_ALL else TaskViewConfig.MATCH_ANY) } },
                        )
                    })
                }
                AbSheetRow("Clear this filter", onClick = { setConfig { it.copy(tags = null) } }, enabled = !config.tags.isNullOrEmpty())
            }

            OptionsPage.History -> {
                val today = now.atZoneSameInstant(zone).toLocalDate()
                val entries = TaskHistoryRules.list(lists, now)
                val finished = entries.filter { it.kind == TaskHistoryKind.FINISHED }
                val gone = entries.filter { it.kind != TaskHistoryKind.FINISHED }
                if (entries.isEmpty()) SheetFoot("Nothing finished or deleted in the last 30 days.")
                if (finished.isNotEmpty()) SheetSectionHead("Finished")
                finished.forEach { entry ->
                    val at = entry.at.atZoneSameInstant(zone)
                    AbSheetRow(entry.task.title, onClick = { controller.doAgain(entry.id) }, subtitle = TaskRowText.due(at.toLocalDate(), at.toLocalTime().withSecond(0).withNano(0), today), trailing = { AbSheetValue("Do again", opens = false) })
                }
                if (gone.isNotEmpty()) SheetSectionHead("Deleted and archived")
                gone.forEach { entry ->
                    val at = entry.at.atZoneSameInstant(zone)
                    AbSheetRow(entry.task.title, onClick = { controller.bringBack(entry.id) }, subtitle = TaskRowText.due(at.toLocalDate(), at.toLocalTime().withSecond(0).withNano(0), today), trailing = { AbSheetValue("Bring back", opens = false) })
                }
            }
        }
    }
}

// One ticked row per choice of a filter.
@Composable
private fun PickRows(choices: List<Pair<String, String>>, picked: List<String>?, onToggle: (String) -> Unit) {
    choices.forEach { (value, name) ->
        AbSheetRow(name, onClick = { onToggle(value) }, role = Role.Checkbox, trailing = { AbSheetCheck(picked.orEmpty().contains(value)) })
    }
}
