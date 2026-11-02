package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.wrapContentHeight
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.stats.HeatCell
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.LocalDate

/**
 * A grid of rounded squares coloured from the theme's five-step heatmap ramp (`Ab.colors.heatmap`, from the tokens). The squares
 * share the width, up to [maxCell]; a cell with level -1 has no data and draws nothing. [columnLabels] sit above ([labelsOnTop]) or
 * below the grid, one per column; [rowLabels] to its left. Tapping a cell that has a date calls [onCellClick]. [description] is
 * what a screen reader says for the whole picture.
 */
@Composable
internal fun HeatGrid(
    cells: List<HeatCell>,
    rows: Int,
    columns: Int,
    rowLabels: List<String>?,
    columnLabels: List<String?>?,
    labelsOnTop: Boolean,
    gap: Dp,
    maxCell: Dp,
    description: String,
    modifier: Modifier = Modifier,
    onCellClick: ((LocalDate) -> Unit)? = null,
) {
    val ramp = Ab.colors.heatmap
    val density = LocalDensity.current
    val byPlace = cells.associateBy { it.row to it.column }
    BoxWithConstraints(modifier.fillMaxWidth()) {
        val labelWidth = if (rowLabels != null) ROW_LABEL_WIDTH else 0.dp
        val cell = minOf(maxCell, (maxWidth - labelWidth - gap * (columns - 1)) / columns)
        val gridWidth = cell * columns + gap * (columns - 1)
        val gridHeight = cell * rows + gap * (rows - 1)
        Column(Modifier.clearAndSetSemantics { contentDescription = description }) {
            if (columnLabels != null && labelsOnTop) ColumnLabels(columnLabels, labelWidth, cell, gap, Modifier.padding(bottom = 4.dp))
            Row {
                if (rowLabels != null) {
                    Column(Modifier.width(labelWidth)) {
                        rowLabels.forEachIndexed { index, label ->
                            Box(Modifier.height(if (index < rows - 1) cell + gap else cell), contentAlignment = Alignment.CenterStart) {
                                Text(
                                    label, style = Ab.type.micro, color = Ab.colors.muted, maxLines = 1, softWrap = false,
                                    modifier = Modifier.wrapContentHeight(Alignment.CenterVertically, unbounded = true),
                                )
                            }
                        }
                    }
                }

                Canvas(
                    Modifier.size(gridWidth, gridHeight).pointerInput(cells, onCellClick) {
                        if (onCellClick == null) return@pointerInput
                        detectTapGestures { at ->
                            val step = with(density) { (cell + gap).toPx() }
                            val column = (at.x / step).toInt().coerceIn(0, columns - 1)
                            val row = (at.y / step).toInt().coerceIn(0, rows - 1)
                            byPlace[row to column]?.date?.let(onCellClick)
                        }
                    },
                ) {
                    val side = cell.toPx()
                    val step = side + gap.toPx()
                    val radius = minOf(4.dp.toPx(), side * 0.28f)
                    for (heat in cells) {
                        if (heat.level < 0) continue
                        drawRoundRect(
                            ramp[heat.level.coerceIn(0, ramp.lastIndex)], Offset(heat.column * step, heat.row * step), Size(side, side), CornerRadius(radius, radius),
                        )
                    }
                }
            }

            if (columnLabels != null && !labelsOnTop) ColumnLabels(columnLabels, labelWidth, cell, gap, Modifier.padding(top = 4.dp))
        }
    }
}

// One label per column, left aligned under (or over) its own column; a label may be wider than its column when its neighbours are empty.
@Composable
private fun ColumnLabels(labels: List<String?>, offset: Dp, cell: Dp, gap: Dp, modifier: Modifier) {
    Row(modifier.padding(start = offset)) {
        labels.forEach { label ->
            Box(Modifier.width(cell + gap)) {
                if (label != null) Text(label, style = Ab.type.micro, color = Ab.colors.muted, maxLines = 1, softWrap = false, modifier = Modifier.wrapContentWidth(Alignment.Start, unbounded = true))
            }
        }
    }
}

/** "Less ▪▪▪▪▪ More": the five steps of the ramp, as the boards draw it. */
@Composable
internal fun HeatLegend(modifier: Modifier = Modifier) {
    Row(modifier.fillMaxWidth().clearAndSetSemantics { }, horizontalArrangement = Arrangement.End, verticalAlignment = Alignment.CenterVertically) {
        Text("Less", style = Ab.type.micro, color = Ab.colors.muted)
        Row(Modifier.padding(horizontal = 8.dp), horizontalArrangement = Arrangement.spacedBy(3.dp)) {
            Ab.colors.heatmap.forEach { color -> Box(Modifier.size(11.dp).background(color, RoundedCornerShape(3.dp))) }
        }
        Text("More", style = Ab.type.micro, color = Ab.colors.muted)
    }
}

private val ROW_LABEL_WIDTH = 38.dp
