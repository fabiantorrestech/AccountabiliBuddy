package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.animation.core.tween
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.layout.boundsInRoot
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.unit.IntOffset
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.Inbox
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.ShelfPage
import com.accountabilibuddy.shared.tasks.TaskItem

/**
 * One page of the Shelf as a card (board Tasks-Phone): the header (the bin's icon in the accent, or a label's 12 dp dot with no tint;
 * the name at 16 sp / 600; "4 · ★1"; a 38 dp "+" that adds to this page) pinned over its own scrolling list of rows. An empty page
 * says so with the tray icon ([emptyText]). The card fills the space it is given, so every page is the same size.
 *
 * [rows] are the rows to draw, in order: the page's tasks, with the dragged ones at the place they would land while a drag is over
 * the list, and a just-ticked one kept until its toast goes. Rows that change place ease there over 180 ms × [motionScale]
 * ("neighbours make room"); with motion off they just take their place. The list reports where it is on the screen ([onListBounds])
 * and scrolls with [listState], so a drag can find its place and scroll the list from its edges.
 */
@Composable
fun ShelfPageCard(
    page: ShelfPage,
    emptyText: String,
    onAdd: () -> Unit,
    modifier: Modifier = Modifier,
    rows: List<TaskItem> = page.tasks,
    listState: LazyListState = rememberLazyListState(),
    motionScale: Float = 1f,
    onListBounds: (Rect) -> Unit = {},
    row: @Composable (TaskItem) -> Unit,
) {
    val colors = Ab.colors
    Column(modifier.fillMaxSize().clip(RoundedCornerShape(24.dp)).background(colors.surface)) {
        Row(
            Modifier.fillMaxWidth().padding(start = 16.dp, top = 12.dp, end = 6.dp, bottom = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            PageMarker(page, iconSize = 19.dp, dotSize = 12.dp)
            Text(
                page.title, style = Ab.type.body.copy(fontSize = 16.sp), fontWeight = FontWeight.SemiBold, color = colors.ink,
                maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false).semantics { heading() },
            )
            Text(page.countText, style = Ab.type.bodyMedium.copy(fontSize = 14.5.sp, fontFeatureSettings = "tnum"), color = colors.faint, maxLines = 1)
            Spacer(Modifier.weight(1f))
            Box(
                Modifier.size(38.dp).clip(CircleShape).clickable(role = Role.Button, onClick = onAdd).semantics { contentDescription = page.addText },
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Outlined.Add, contentDescription = null, tint = colors.ink, modifier = Modifier.size(22.dp))
            }
        }
        if (rows.isEmpty()) {
            ShelfEmpty(emptyText, Modifier.weight(1f).onGloballyPositioned { onListBounds(it.boundsInRoot()) })
        } else {
            val shift = if (motionScale > 0f) tween<IntOffset>((180 * motionScale).toInt()) else null
            val fade = if (motionScale > 0f) tween<Float>((300 * motionScale).toInt()) else null
            LazyColumn(
                Modifier.weight(1f).fillMaxWidth().onGloballyPositioned { onListBounds(it.boundsInRoot()) },
                state = listState,
                contentPadding = PaddingValues(start = 12.dp, end = 12.dp, bottom = 84.dp),
            ) {
                items(rows, key = { it.id }) { task ->
                    Box(Modifier.animateItem(fadeInSpec = null, placementSpec = shift, fadeOutSpec = fade)) { row(task) }
                }
            }
        }
    }
}

/** An empty page or an empty Shelf: the tray icon and one line ("Nothing to see here.", "Nothing matches."). */
@Composable
fun ShelfEmpty(text: String, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Column(
        modifier.fillMaxWidth().padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(10.dp, Alignment.CenterVertically),
    ) {
        Icon(Icons.Outlined.Inbox, contentDescription = null, tint = colors.faint, modifier = Modifier.size(34.dp))
        Text(text, style = Ab.type.bodyMedium, color = colors.muted, textAlign = TextAlign.Center)
    }
}
