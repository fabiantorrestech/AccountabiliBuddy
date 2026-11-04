package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.today.TodayItem
import com.accountabilibuddy.companion.today.TodayLayout
import com.accountabilibuddy.companion.today.TodayPlacement
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * "Today layout" (Settings › Appearance): the parts of Today in three groups, as the stats views sheet does for its chips. Drag a
 * handle (or use Move up and Move down with a screen reader) to put the parts of a group in order; the arrow moves a part into
 * "More today" or back above it; the eye hides it, and brings it back where it normally sits. A part that carries a guard or an
 * obligation shows a lock instead of the eye: it can't be hidden (the guard banner, the lunch chip and the link-a-PC card can't be folded either).
 * It only changes what Today draws; no guard, wait or lock is touched, and nothing is synced.
 */
@Composable
fun TodayLayoutSheet(layout: TodayLayout, onChange: (TodayLayout) -> Unit, onDismiss: () -> Unit) {
    val colors = Ab.colors
    val type = Ab.type
    AbSheet(onDismiss = onDismiss) {
        Text("Today layout", style = type.head, color = colors.ink)
        Text(
            "Drag to reorder. A lock means the part always shows: it carries a guard or something you owe. What a running session says is always first.",
            style = type.bodyMedium, color = colors.muted,
        )
        Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState())) {
            Group("Shown on Today", TodayPlacement.Above, layout, onChange)
            Group("In More today", TodayPlacement.Fold, layout, onChange)
            Group("Hidden", TodayPlacement.Hidden, layout, onChange)
            if (!layout.isDefault) {
                TextButton(onClick = { onChange(TodayLayout()) }, modifier = Modifier.padding(top = 8.dp)) {
                    Text("Back to the usual order", color = colors.ink)
                }
            }
        }
    }
}

@Composable
private fun Group(title: String, placement: TodayPlacement, layout: TodayLayout, onChange: (TodayLayout) -> Unit) {
    val items = layout.editorItems(placement)
    Column(Modifier.fillMaxWidth().padding(top = 12.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
        AbSectionLabel(title)
        if (items.isEmpty()) {
            Text(if (placement == TodayPlacement.Hidden) "Nothing hidden." else "Nothing here.", style = Ab.type.small, color = Ab.colors.faint, modifier = Modifier.padding(vertical = 8.dp))
        }
        ReorderableRows(
            items = items,
            onReorder = { onChange(layout.withGroupOrder(placement, it)) },
            label = { it.title },
            // A hidden part can't be dragged: its order only matters once it shows again.
            draggable = placement != TodayPlacement.Hidden,
        ) { part, handle ->
            TodayLayoutRow(part, placement, handle, onFold = { onChange(layout.toggleFolded(part)) }, onHide = { onChange(layout.toggleHidden(part)) })
        }
    }
}
