package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.GridView
import androidx.compose.material.icons.outlined.Tag
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.layout.boundsInRoot
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.ShelfPickerTile

/**
 * The bottom bar while a row is dragged (boards Tasks-Phone-Drag-Bin and -Drag-Pick; change note §4c-1): two zones in the tabs'
 * place. On the left "Hold to pick a bin" (a grid icon, dashed): holding the row there opens the picker of every page; it then
 * reads "Release on a bin above" in the accent. On the right the red **Cancel** ("keeps selected", dashed; a solid red border
 * while the row is over it): a release there moves nothing and keeps the selection.
 *
 * It fills the place it is given (the tabs' own size, so nothing above moves) and reports where its two zones are.
 */
@Composable
fun DragBar(text: String, pickerOpen: Boolean, overPick: Boolean, overCancel: Boolean, onPickBounds: (Rect) -> Unit, onCancelBounds: (Rect) -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val shape = RoundedCornerShape(18.dp)
    val red = colors.drift
    Row(
        modifier.fillMaxSize().navigationBarsPadding().padding(horizontal = 10.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        val lit = pickerOpen || overPick
        Row(
            Modifier.weight(1f).fillMaxHeight().clip(shape)
                .background(if (lit) colors.accent.copy(alpha = 0.12f) else Color.Transparent)
                .then(if (pickerOpen) Modifier.border(2.dp, colors.accent, shape) else Modifier.dashedBorder(if (lit) colors.accent else colors.line, 2.dp, 18.dp))
                .onGloballyPositioned { onPickBounds(it.boundsInRoot()) }
                .padding(horizontal = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Icon(Icons.Outlined.GridView, contentDescription = null, tint = if (lit) colors.accent else colors.muted, modifier = Modifier.size(20.dp))
            Text(text, style = Ab.type.bodyMedium.copy(fontSize = 14.sp), fontWeight = FontWeight.SemiBold, color = if (lit) colors.accent else colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        Column(
            Modifier.width(112.dp).fillMaxHeight().clip(shape).background(red.copy(alpha = if (overCancel) 0.2f else 0.1f))
                .then(if (overCancel) Modifier.border(2.dp, red, shape) else Modifier.dashedBorder(red, 2.dp, 18.dp))
                .onGloballyPositioned { onCancelBounds(it.boundsInRoot()) }
                .semantics { contentDescription = "Cancel the drag, keep them selected" },
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(1.dp, Alignment.CenterVertically),
        ) {
            Icon(Icons.Outlined.Close, contentDescription = null, tint = red, modifier = Modifier.size(18.dp))
            Text("Cancel", style = Ab.type.small.copy(fontSize = 13.sp), fontWeight = FontWeight.Bold, color = red, maxLines = 1)
            Text("keeps selected", style = Ab.type.micro.copy(fontSize = 10.5.sp, letterSpacing = 0.sp), color = red.copy(alpha = 0.85f), maxLines = 1)
        }
    }
}

/**
 * The picker a held drag raises above the bottom bar (board Tasks-Phone-Drag-Pick): a handle, the [title] ("Drop on a label") with
 * "Drag back up to close this", a grid of 58 dp tiles (three in a row on a phone, more when there is room), each with its dot or
 * icon, its name and its count; the page showing is dashed and dimmed, "You are here". The tile under the finger ([overKey]) lifts:
 * outlined in its colour and a little larger. A [note] at the foot. Each tile reports where it is, and the sheet too.
 */
@Composable
fun DragPicker(
    title: String,
    note: String,
    tiles: List<ShelfPickerTile>,
    overKey: String?,
    onBounds: (Rect) -> Unit,
    onTileBounds: (String, Rect) -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Ab.colors
    val shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp)
    Column(
        modifier.fillMaxWidth().shadow(24.dp, shape).clip(shape).background(colors.surface).onGloballyPositioned { onBounds(it.boundsInRoot()) }
            .padding(start = 16.dp, end = 16.dp, top = 10.dp, bottom = 20.dp),
    ) {
        Box(Modifier.align(Alignment.CenterHorizontally).padding(bottom = 12.dp).size(width = 36.dp, height = 4.dp).background(colors.faint, CircleShape))
        Row(Modifier.padding(start = 4.dp, end = 4.dp, bottom = 10.dp), verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(title, style = Ab.type.body.copy(fontSize = 16.sp), fontWeight = FontWeight.SemiBold, color = colors.ink, maxLines = 1, modifier = Modifier.semantics { heading() })
            Text("Drag back up to close this", style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        BoxWithConstraints(Modifier.fillMaxWidth()) {
            // Three in a row on a phone (the board); a wider page fits more, about 130 dp each.
            val columns = (maxWidth / 130.dp).toInt().coerceIn(3, 6)
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                tiles.chunked(columns).forEach { row ->
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        row.forEach { tile -> PickerTile(tile, over = tile.page.key == overKey && !tile.here, onBounds = { onTileBounds(tile.page.key, it) }, modifier = Modifier.weight(1f)) }
                        repeat(columns - row.size) { Box(Modifier.weight(1f)) }
                    }
                }
            }
        }
        Text(note, style = Ab.type.small.copy(fontSize = 12.sp), color = colors.faint, modifier = Modifier.padding(start = 4.dp, end = 4.dp, top = 12.dp))
    }
}

@Composable
private fun PickerTile(tile: ShelfPickerTile, over: Boolean, onBounds: (Rect) -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val page = tile.page
    val shape = RoundedCornerShape(16.dp)
    val tint = labelColor(page.labelColor) ?: colors.accent
    val scale = if (over) 1.04f else 1f
    Column(
        modifier
            .height(58.dp)
            .onGloballyPositioned { onBounds(it.boundsInRoot()) }
            .graphicsLayer { scaleX = scale; scaleY = scale; alpha = if (tile.here) 0.45f else 1f }
            .clip(shape)
            .then(
                when {
                    tile.here -> Modifier.dashedBorder(colors.line, 1.dp, 16.dp)
                    over -> Modifier.background(lerp(colors.surface2, tint, 0.22f)).border(2.dp, tint, shape)
                    else -> Modifier.background(colors.surface2).border(1.dp, colors.line, shape)
                },
            )
            .padding(horizontal = 12.dp)
            .semantics { contentDescription = "Drop on ${page.title}, ${tile.detail}" },
        verticalArrangement = Arrangement.spacedBy(2.dp, Alignment.CenterVertically),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(7.dp)) {
            val bin = page.bin
            when {
                bin != null -> Icon(binIcon(bin), contentDescription = null, tint = colors.accent, modifier = Modifier.size(16.dp))
                page.tag != null -> Icon(Icons.Outlined.Tag, contentDescription = null, tint = colors.muted, modifier = Modifier.size(16.dp))
                else -> LabelDot(page.labelColor, 10.dp)
            }
            Text(page.title, style = Ab.type.bodyMedium.copy(fontSize = 14.sp), fontWeight = FontWeight.SemiBold, color = colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        Text(tile.detail, style = Ab.type.small.copy(fontSize = 11.5.sp), color = colors.muted, maxLines = 1)
    }
}
