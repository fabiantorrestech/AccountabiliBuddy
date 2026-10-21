package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.isSystemInDarkTheme
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
import java.util.UUID

private const val MINUTES_PER_DAY = 24 * 60

/** A drag in progress; whether the part crossed midnight is decided when it starts and kept (as on the PC). */
private data class BarDrag(val partId: UUID, val kind: BarGrabKind, val start: Int, val end: Int, val grabOffset: Int, val crossesMidnight: Boolean)

/**
 * The phone's 24-hour drag bar for one day template (like the PC's DayPlanBar): tap empty space to add a part, tap a
 * part to select it, drag an edge to resize it or its middle to move it; snaps to 15 minutes and never runs into
 * another part ([DayTemplateEditing]). A part crossing midnight shows at both ends and can only be resized.
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
    val dark = isSystemInDarkTheme()
    val track = if (dark) Color(0x28FFFFFF) else Color(0x1C000000)
    val axisText = if (dark) Color(0xBEFFFFFF) else Color(0xAA000000)
    val edgeGrabPx = with(androidx.compose.ui.platform.LocalDensity.current) { 14.dp.toPx() }

    Canvas(
        modifier
            .fillMaxWidth()
            .height(64.dp)
            // Dragging near the screen's edges (parts at midnight) must not become the system Back gesture.
            .systemGestureExclusion()
            .pointerInput(Unit) {
                detectTapGestures { offset ->
                    val minute = (offset.x / size.width * MINUTES_PER_DAY).toInt().coerceIn(0, MINUTES_PER_DAY)
                    val piece = DayBarGrab.pieces(currentTemplate).firstOrNull { minute >= it.start && minute < it.end }
                    if (piece != null) {
                        select(piece.partId)
                        return@detectTapGestures
                    }

                    val category = currentCategories.firstOrNull { it.behavior == CategoryBehavior.Rest } ?: currentCategories.firstOrNull()
                    val added = category?.let { DayTemplateEditing.addAt(currentTemplate, minute, it.id) }
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
                        val (piece, kind) = DayBarGrab.pick(DayBarGrab.pieces(currentTemplate), offset.x, size.width.toFloat(), edgeGrabPx)
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
                            BarGrabKind.Start -> DayTemplateEditing.move(currentTemplate, current.partId, minute, current.end, current.crossesMidnight)
                            BarGrabKind.End -> DayTemplateEditing.move(currentTemplate, current.partId, current.start, minute, current.crossesMidnight)
                            BarGrabKind.Whole -> {
                                val length = current.end - current.start
                                val start = DayTemplateEditing.snap((minute - current.grabOffset).toDouble()).coerceIn(0, MINUTES_PER_DAY - length)
                                DayTemplateEditing.move(currentTemplate, current.partId, start, start + length, crossesMidnight = false)
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
        val barTop = 2.dp.toPx()
        val barHeight = 34.dp.toPx()
        val corner = CornerRadius(5.dp.toPx())
        fun xOf(minute: Int) = minute.toFloat() / MINUTES_PER_DAY * size.width

        drawRoundRect(track, Offset(0f, barTop), Size(size.width, barHeight), corner)
        for (piece in DayBarGrab.pieces(template)) {
            val part = template.parts.first { it.id == piece.partId }
            val category = categories.firstOrNull { it.id == part.categoryId }
            val color = Color(((category?.argb ?: 0xFF64748BL) and 0x00FFFFFFL or 0xD9000000L).toInt())
            val left = xOf(piece.start)
            val width = (xOf(piece.end) - left).coerceAtLeast(1f)
            drawRoundRect(color, Offset(left, barTop), Size(width, barHeight), corner)
            if (part.id == selected) {
                drawRoundRect(Color.White, Offset(left, barTop), Size(width, barHeight), corner, style = Stroke(2.dp.toPx()))
            }

            val name = part.label ?: category?.name.orEmpty()
            if (width > 28.dp.toPx() && name.isNotEmpty()) {
                val layout = measurer.measure(
                    name, TextStyle(color = Color.White, fontSize = 11.sp), maxLines = 1, overflow = TextOverflow.Ellipsis,
                    constraints = Constraints(maxWidth = (width - 8.dp.toPx()).toInt().coerceAtLeast(0)),
                )
                drawText(layout, topLeft = Offset(left + 4.dp.toPx(), barTop + (barHeight - layout.size.height) / 2))
            }
        }

        for (hour in 0..24 step 3) {
            val x = xOf(hour * 60)
            val label = "${if (hour % 12 == 0) 12 else hour % 12}${if (hour % 24 < 12) "a" else "p"}"
            val layout = measurer.measure(label, TextStyle(color = axisText, fontSize = 10.sp))
            val left = (x - layout.size.width / 2).coerceIn(0f, (size.width - layout.size.width).coerceAtLeast(0f))
            drawText(layout, topLeft = Offset(left, barTop + barHeight + 4.dp.toPx()))
        }
    }
}
