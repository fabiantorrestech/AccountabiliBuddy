package com.accountabilibuddy.companion.ui

import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.DragIndicator
import androidx.compose.material3.Icon
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.CustomAccessibilityAction
import androidx.compose.ui.semantics.customActions
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.zIndex
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlin.math.roundToInt

/**
 * A column of rows that can be dragged into a new order by a handle, the way the Stats views sheet does it (and with Move up and
 * Move down for a screen reader). While a row is dragged the order is worked out here; [onReorder] gets the new order when the
 * finger lifts. [row] draws one row and puts [handle] (the drag handle, empty when not [draggable]) where it wants it.
 */
@Composable
fun <T : Any> ReorderableRows(
    items: List<T>,
    onReorder: (List<T>) -> Unit,
    label: (T) -> String,
    draggable: Boolean = true,
    /**
     * Drag by holding the row itself instead of a handle (board Goals-Phone-Area: "Press and hold a goal to drag it into a new
     * order"): no handle is drawn, the lift gives a haptic tick, and Move up / Move down are the row's own actions.
     */
    holdToDrag: Boolean = false,
    row: @Composable (item: T, handle: @Composable () -> Unit) -> Unit,
) {
    val density = LocalDensity.current
    val haptics = LocalHapticFeedback.current
    // The height of a row in pixels, as drawn (a subtitle may wrap with large text): one row is the step of a drag.
    var rowPx by remember { mutableFloatStateOf(with(density) { 64.dp.toPx() }) }
    // A row's gesture lives as long as the row, so it must read the newest items and callback.
    val currentItems by rememberUpdatedState(items)
    val currentOnReorder by rememberUpdatedState(onReorder)
    var working by remember { mutableStateOf(items) }
    var dragging by remember { mutableStateOf<T?>(null) }
    var offset by remember { mutableFloatStateOf(0f) }
    // Under a finger the order is the working one; otherwise it is always what the caller holds.
    val shown = if (dragging != null) working else items

    Column(Modifier.fillMaxWidth()) {
        shown.forEachIndexed { index, item ->
            // Keyed by the item, so a row that moves while it is dragged keeps its gesture.
            key(item) {
                val isDragged = dragging == item
                val hold = if (draggable && holdToDrag) {
                    Modifier
                        .semantics {
                            customActions = listOf(
                                CustomAccessibilityAction("Move up") { if (index > 0) currentOnReorder(currentItems.toMutableList().apply { add(index - 1, removeAt(index)) }); true },
                                CustomAccessibilityAction("Move down") { if (index < currentItems.lastIndex) currentOnReorder(currentItems.toMutableList().apply { add(index + 1, removeAt(index)) }); true },
                            )
                        }
                        .pointerInput(item) {
                            detectDragGesturesAfterLongPress(
                                onDragStart = {
                                    working = currentItems
                                    dragging = item
                                    offset = 0f
                                    haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                                },
                                onDragEnd = {
                                    dragging = null
                                    offset = 0f
                                    currentOnReorder(working)
                                },
                                onDragCancel = { dragging = null; offset = 0f },
                            ) { change, drag ->
                                change.consume()
                                offset += drag.y
                                val current = working.indexOf(item)
                                val target = (current + (offset / rowPx).roundToInt()).coerceIn(0, working.lastIndex)
                                if (target != current) {
                                    working = working.toMutableList().apply { add(target, removeAt(current)) }
                                    offset -= (target - current) * rowPx
                                }
                            }
                        }
                } else {
                    Modifier
                }
                val handle: @Composable () -> Unit = {
                    if (draggable && !holdToDrag) {
                        Box(
                            Modifier
                                .size(44.dp)
                                // On the handle, which has the description a screen reader reads: Move up and Move down are its actions.
                                .semantics {
                                    customActions = listOf(
                                        CustomAccessibilityAction("Move up") { if (index > 0) currentOnReorder(currentItems.toMutableList().apply { add(index - 1, removeAt(index)) }); true },
                                        CustomAccessibilityAction("Move down") { if (index < currentItems.lastIndex) currentOnReorder(currentItems.toMutableList().apply { add(index + 1, removeAt(index)) }); true },
                                    )
                                }
                                .pointerInput(item) {
                                    detectDragGestures(
                                        onDragStart = { working = currentItems; dragging = item; offset = 0f },
                                        onDragEnd = {
                                            dragging = null
                                            offset = 0f
                                            currentOnReorder(working)
                                        },
                                        onDragCancel = { dragging = null; offset = 0f },
                                    ) { change, drag ->
                                        change.consume()
                                        offset += drag.y
                                        val current = working.indexOf(item)
                                        val target = (current + (offset / rowPx).roundToInt()).coerceIn(0, working.lastIndex)
                                        if (target != current) {
                                            working = working.toMutableList().apply { add(target, removeAt(current)) }
                                            offset -= (target - current) * rowPx
                                        }
                                    }
                                },
                            contentAlignment = Alignment.Center,
                        ) {
                            Icon(Icons.Outlined.DragIndicator, contentDescription = "Drag ${label(item)} to reorder", tint = Ab.colors.faint, modifier = Modifier.size(22.dp))
                        }
                    }
                }
                Box(
                    Modifier
                        .fillMaxWidth()
                        .onSizeChanged { if (index == 0) rowPx = it.height.toFloat() }
                        .zIndex(if (isDragged) 1f else 0f)
                        .graphicsLayer { translationY = if (isDragged) offset else 0f }
                        .then(hold),
                ) {
                    row(item, handle)
                }
            }
        }
    }
}
