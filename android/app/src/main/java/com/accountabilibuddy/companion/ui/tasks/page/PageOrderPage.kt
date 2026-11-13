package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.tasks.TasksUiState
import com.accountabilibuddy.companion.ui.ReorderableRows
import com.accountabilibuddy.companion.ui.kit.AbSwitch
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.ShelfPage
import com.accountabilibuddy.shared.tasks.ShelfPages
import com.accountabilibuddy.shared.tasks.ShelfPaging
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskPageOrder
import com.accountabilibuddy.shared.tasks.TaskViewConfig
import java.time.OffsetDateTime
import java.time.ZoneId

private val GROUPINGS = listOf(TaskViewConfig.GROUP_BINS to "Bins", TaskViewConfig.GROUP_LABEL to "Labels", TaskViewConfig.GROUP_TAG to "Tags")

/**
 * Options › Page order (board Tasks-Phone-Order, change note §2 and §4c): a Bins | Labels | Tags switch (each grouping keeps its own
 * order, start page and wrap around), then one 50 dp row per page: ≡ to drag it left or right in the order, its dot or icon, its
 * name, how many tasks it holds, and a radio that makes it the page Tasks opens on (tagged "Start page"). Under the list the Wrap
 * around switch with what it does, and the note about the dots becoming a count. All of it is kept on this device only.
 */
@Composable
fun PageOrderPage(
    state: TasksUiState,
    onState: ((TasksUiState) -> TasksUiState) -> Unit,
    lists: TaskLists,
    now: OffsetDateTime,
    zone: ZoneId,
    grouping: String,
    onGrouping: (String) -> Unit,
) {
    val colors = Ab.colors
    val stored = state.orders[grouping]
    // The pages this grouping has now, whatever the view in use filters out: the order is set for all of them.
    val pages = ShelfPages.build(lists, TaskViewConfig.EVERYTHING.copy(group = grouping), now, zone, storedOrder = stored)
    val keys = pages.map { it.key }
    val start = pages.getOrNull(ShelfPaging.startIndex(keys, state.starts[grouping]))?.key

    Row(Modifier.fillMaxWidth().padding(start = 24.dp, end = 24.dp, top = 8.dp, bottom = 4.dp).border(1.dp, colors.line, CircleShape).padding(3.dp)) {
        GROUPINGS.forEach { (value, name) ->
            val chosen = value == grouping
            Box(
                Modifier.weight(1f).height(38.dp).clip(CircleShape).background(if (chosen) colors.surface2 else colors.surface)
                    .clickable(role = Role.Tab) { onGrouping(value) }.semantics { selected = chosen },
                contentAlignment = Alignment.Center,
            ) {
                Text(name, style = Ab.type.small.copy(fontSize = 13.5.sp), fontWeight = FontWeight.SemiBold, color = if (chosen) colors.ink else colors.muted)
            }
        }
    }

    Column(Modifier.padding(horizontal = 12.dp)) {
        if (pages.isEmpty()) {
            Text("No pages yet: no task has one of these.", style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(12.dp))
        }
        ReorderableRows(
            items = pages,
            onReorder = { ordered -> onState { it.copy(orders = it.orders + (grouping to TaskPageOrder.reordered(keys, stored, ordered.map { page -> page.key }))) } },
            label = { it.title },
        ) { page, handle ->
            PageOrderRow(page, isStart = page.key == start, handle = handle, onStart = { onState { it.copy(starts = it.starts + (grouping to page.key)) } })
        }
    }

    HorizontalDivider(color = colors.line, thickness = 1.dp, modifier = Modifier.padding(horizontal = 24.dp))
    Row(
        Modifier.fillMaxWidth().clickable(role = Role.Switch) { onState { it.copy(wraps = it.wraps + (grouping to !it.wrapOf(grouping))) } }.padding(horizontal = 24.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Column(Modifier.weight(1f)) {
            Text("Wrap around", style = Ab.type.bodyMedium.copy(fontSize = 15.sp), fontWeight = FontWeight.Medium, color = colors.ink)
            Text(
                "Swiping or dragging past the last page goes to the first, and the other way round. When on, the end pages peek at each other.",
                style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted,
            )
        }
        AbSwitch(state.wrapOf(grouping))
    }
    HorizontalDivider(color = colors.line, thickness = 1.dp, modifier = Modifier.padding(horizontal = 24.dp))
    Row(Modifier.padding(horizontal = 24.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("With more than 5 pages, the dots become a count:", style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted, modifier = Modifier.weight(1f, fill = false))
        Text(
            "4 / 9", style = Ab.type.small.copy(fontFeatureSettings = "tnum"), fontWeight = FontWeight.SemiBold, color = colors.ink,
            modifier = Modifier.border(1.dp, colors.line, CircleShape).padding(horizontal = 10.dp, vertical = 2.dp),
        )
    }
}

// One page of the order: ≡, its marker, its name (and "Start page"), its count, and the start radio.
@Composable
private fun PageOrderRow(page: ShelfPage, isStart: Boolean, handle: @Composable () -> Unit, onStart: () -> Unit) {
    val colors = Ab.colors
    Row(Modifier.fillMaxWidth().heightIn(min = 50.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        handle()
        Box(Modifier.size(18.dp), contentAlignment = Alignment.Center) { PageMarker(page, iconSize = 16.dp, dotSize = 8.dp) }
        Row(Modifier.weight(1f), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(page.title, style = Ab.type.bodyMedium.copy(fontSize = 15.sp), color = colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
            if (isStart) {
                Text(
                    "Start page", style = Ab.type.micro.copy(fontSize = 11.sp, letterSpacing = 0.sp), fontWeight = FontWeight.SemiBold, color = colors.accent, maxLines = 1,
                    modifier = Modifier.background(colors.accent.copy(alpha = 0.16f), CircleShape).padding(horizontal = 8.dp, vertical = 2.dp),
                )
            }
        }
        Text(page.count.toString(), style = Ab.type.small.copy(fontFeatureSettings = "tnum"), color = colors.muted)
        Box(
            Modifier.size(44.dp).clip(CircleShape).clickable(role = Role.RadioButton, onClick = onStart).semantics { selected = isStart; contentDescription = "Start on ${page.title}" },
            contentAlignment = Alignment.Center,
        ) {
            Box(Modifier.size(22.dp).border(2.dp, if (isStart) colors.accent else colors.faint, CircleShape), contentAlignment = Alignment.Center) {
                if (isStart) Box(Modifier.size(10.dp).background(colors.accent, CircleShape))
            }
        }
    }
}
