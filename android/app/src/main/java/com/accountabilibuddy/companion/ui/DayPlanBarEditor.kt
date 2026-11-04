package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.systemGestureExclusion
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.engine.BarGrabKind
import com.accountabilibuddy.companion.engine.DayBarGrab
import com.accountabilibuddy.companion.engine.DayTemplateEditing
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.DayTemplate
import com.accountabilibuddy.companion.ui.theme.Ab
import java.util.UUID

private const val MINUTES_PER_DAY = 24 * 60
private const val BAR_TOP_DP = 2
private const val SINGLE_ROW_DP = 34
private const val ROW_DP = 26
private const val ROW_GAP_DP = 2
private const val ADD_STRIP_DP = 16
private const val AXIS_DP = 24

// The pieces on the row under the finger; none on the strip under the rows, which only adds.
private fun piecesInRow(template: DayTemplate, y: Float, density: Float): List<com.accountabilibuddy.companion.engine.BarPiece> {
    val lanes = DayBarGrab.lanes(template)
    val rows = (lanes.values.maxOrNull() ?: 0) + 1
    val rowStep = ((if (rows == 1) SINGLE_ROW_DP else ROW_DP) + ROW_GAP_DP) * density
    val row = ((y - BAR_TOP_DP * density) / rowStep).toInt()
    if (y < 0f || row >= rows) return emptyList()
    return DayBarGrab.pieces(template).filter { lanes[it.partId] == row }
}

/** A drag in progress; whether the part crossed midnight is decided when it starts and kept (as on the PC). */
private data class BarDrag(val partId: UUID, val kind: BarGrabKind, val start: Int, val end: Int, val grabOffset: Int, val crossesMidnight: Boolean)

/**
 * The phone's 24-hour drag bar for one day template (like the PC's DayPlanBar): tap empty space to add a part, the strip under the
 * rows to lay a new part over what's there (ADR 63: the weaker rules win), tap a part to select it, drag an edge to resize it or its
 * middle to move it; snaps to 15 minutes ([DayTemplateEditing]). Parts that overlap get rows of their own. A part crossing midnight
 * shows at both ends and can only be resized.
 */
@Composable
fun DayPlanBarEditor(
    template: DayTemplate,
    categories: List<DayCategory>,
    selected: UUID?,
    onSelect: (UUID?) -> Unit,
    onChange: (DayTemplate) -> Unit,
    onRefused: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    // Gestures keep running across edits, so they read the latest values (and callbacks) instead of restarting.
    val currentTemplate by rememberUpdatedState(template)
    val currentCategories by rememberUpdatedState(categories)
    val select by rememberUpdatedState(onSelect)
    val edit by rememberUpdatedState(onChange)
    val refuse by rememberUpdatedState(onRefused)
    val drag = remember { arrayOfNulls<BarDrag>(1) }
    val measurer = rememberTextMeasurer()
    // The theme's tones: the track is the second tone, the hour labels muted, the picked part ringed in ink.
    val track = Ab.colors.surface2
    val axisText = Ab.colors.muted
    val ring = Ab.colors.ink
    val fallback = Ab.colors.plan
    val edgeGrabPx = with(androidx.compose.ui.platform.LocalDensity.current) { 14.dp.toPx() }
    val rows = (DayBarGrab.lanes(template).values.maxOrNull() ?: 0) + 1
    val rowHeightDp = if (rows == 1) SINGLE_ROW_DP else ROW_DP
    val barHeightDp = rows * rowHeightDp + (rows - 1) * ROW_GAP_DP

    Canvas(
        modifier
            .fillMaxWidth()
            .height((BAR_TOP_DP + barHeightDp + ROW_GAP_DP + ADD_STRIP_DP + AXIS_DP).dp)
            // Dragging near the screen's edges (parts at midnight) must not become the system Back gesture.
            .systemGestureExclusion()
            .pointerInput(Unit) {
                detectTapGestures { offset ->
                    val minute = (offset.x / size.width * MINUTES_PER_DAY).toInt().coerceIn(0, MINUTES_PER_DAY)
                    val piece = piecesInRow(currentTemplate, offset.y, density).firstOrNull { minute >= it.start && minute < it.end }
                    if (piece != null) {
                        select(piece.partId)
                        return@detectTapGestures
                    }

                    val category = currentCategories.firstOrNull { it.behavior == CategoryBehavior.Rest } ?: currentCategories.firstOrNull()
                    val added = category?.let { DayTemplateEditing.addAt(currentTemplate, minute, it.id, currentCategories) }
                    if (added == null) {
                        refuse("There's no free time there for a new part.")
                    } else {
                        edit(added)
                        select(added.parts.last().id)
                    }
                }
            }
            .pointerInput(Unit) {
                detectDragGestures(
                    onDragStart = { offset ->
                        val (piece, kind) = DayBarGrab.pick(piecesInRow(currentTemplate, offset.y, density), offset.x, size.width.toFloat(), edgeGrabPx)
                            ?: return@detectDragGestures
                        val part = currentTemplate.parts.first { it.id == piece.partId }
                        val start = DayTemplateEditing.minutesOf(part.start)
                        val end = DayTemplateEditing.endMinutes(part)
                        val minute = (offset.x / size.width * MINUTES_PER_DAY).toInt()
                        select(part.id)
                        drag[0] = BarDrag(part.id, kind, start, end, minute - start, part.crossesMidnight)
                    },
                    onDragEnd = { drag[0] = null },
                    onDragCancel = { drag[0] = null },
                    onDrag = { change, _ ->
                        val current = drag[0] ?: return@detectDragGestures
                        change.consume()
                        val minute = (change.position.x / size.width * MINUTES_PER_DAY).toInt().coerceIn(0, MINUTES_PER_DAY)
                        val moved = when (current.kind) {
                            BarGrabKind.Start -> DayTemplateEditing.move(currentTemplate, current.partId, minute, current.end, current.crossesMidnight, currentCategories, stickToNeighbours = true)
                            BarGrabKind.End -> DayTemplateEditing.move(currentTemplate, current.partId, current.start, minute, current.crossesMidnight, currentCategories, stickToNeighbours = true)
                            BarGrabKind.Whole -> {
                                val length = current.end - current.start
                                val start = DayTemplateEditing.snap((minute - current.grabOffset).toDouble()).coerceIn(0, MINUTES_PER_DAY - length)
                                DayTemplateEditing.move(currentTemplate, current.partId, start, start + length, crossesMidnight = false, categories = currentCategories)
                                    ?.takeIf { result ->
                                        val part = result.parts.first { it.id == current.partId }
                                        DayTemplateEditing.endMinutes(part) - DayTemplateEditing.minutesOf(part.start) == length
                                    }
                            }
                        }
                        moved?.let { edit(it) }
                    },
                )
            },
    ) {
        val barTop = BAR_TOP_DP.dp.toPx()
        val barHeight = barHeightDp.dp.toPx()
        val rowHeight = rowHeightDp.dp.toPx()
        val rowStep = (rowHeightDp + ROW_GAP_DP).dp.toPx()
        val lanes = DayBarGrab.lanes(template)
        val corner = CornerRadius(5.dp.toPx())
        fun xOf(minute: Int) = minute.toFloat() / MINUTES_PER_DAY * size.width

        drawRoundRect(track, Offset(0f, barTop), Size(size.width, barHeight), corner)
        val stripTop = barTop + barHeight + ROW_GAP_DP.dp.toPx()
        drawRoundRect(track, Offset(0f, stripTop), Size(size.width, ADD_STRIP_DP.dp.toPx()), CornerRadius(3.dp.toPx()))
        for (piece in DayBarGrab.pieces(template)) {
            val part = template.parts.first { it.id == piece.partId }
            val category = categories.firstOrNull { it.id == part.categoryId }
            // The category's own colour (the user chose it); a part with no category takes the plan tone.
            val color = category?.let { Color((it.argb and 0x00FFFFFFL or 0xD9000000L).toInt()) } ?: fallback
            val left = xOf(piece.start)
            val width = (xOf(piece.end) - left).coerceAtLeast(1f)
            val top = barTop + (lanes[part.id] ?: 0) * rowStep
            drawRoundRect(color, Offset(left, top), Size(width, rowHeight), corner)
            if (part.id == selected) {
                drawRoundRect(ring, Offset(left, top), Size(width, rowHeight), corner, style = Stroke(2.dp.toPx()))
            }

            val name = part.label ?: category?.name.orEmpty()
            if (width > 28.dp.toPx() && name.isNotEmpty()) {
                val layout = measurer.measure(
                    name, TextStyle(color = if (color.luminance() > 0.5f) Color.Black else Color.White, fontSize = 11.sp), maxLines = 1, overflow = TextOverflow.Ellipsis,
                    constraints = Constraints(maxWidth = (width - 8.dp.toPx()).toInt().coerceAtLeast(0)),
                )
                drawText(layout, topLeft = Offset(left + 4.dp.toPx(), top + (rowHeight - layout.size.height) / 2))
            }
        }

        for (hour in 0..24 step 3) {
            val x = xOf(hour * 60)
            val label = "${if (hour % 12 == 0) 12 else hour % 12}${if (hour % 24 < 12) "a" else "p"}"
            val layout = measurer.measure(label, TextStyle(color = axisText, fontSize = 10.sp))
            val left = (x - layout.size.width / 2).coerceIn(0f, (size.width - layout.size.width).coerceAtLeast(0f))
            drawText(layout, topLeft = Offset(left, stripTop + ADD_STRIP_DP.dp.toPx() + 4.dp.toPx()))
        }
    }
}
