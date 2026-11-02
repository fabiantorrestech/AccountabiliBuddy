package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.DragIndicator
import androidx.compose.material.icons.outlined.Visibility
import androidx.compose.material.icons.outlined.VisibilityOff
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.key
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
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
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.customActions
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.zIndex
import com.accountabilibuddy.companion.stats.StatsView
import com.accountabilibuddy.companion.stats.StatsViewsState
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlin.math.roundToInt

/**
 * "Stats views" (board 19h): drag a handle to put the views in the order the chips show them, and the eye to hide one. A hidden view
 * stays in this list, one tap away: tapping a row opens it. At least one view stays shown. Each row also has Move up and Move down
 * for a screen reader. Personal bests is not listed: the phone has no such numbers yet.
 */
@Composable
fun StatsViewsSheet(state: StatsViewsState, onChange: (StatsViewsState) -> Unit, onOpen: (StatsView) -> Unit, onDismiss: () -> Unit) {
    val colors = Ab.colors
    val type = Ab.type
    val density = LocalDensity.current
    // The height of a row in pixels, as drawn (a subtitle may wrap with large text): one row is the step of a drag.
    var rowPx by remember { mutableFloatStateOf(with(density) { ROW_HEIGHT.toPx() }) }
    // The row's gesture lives as long as the row, so it must read the newest state and callback, never the ones of the composition it began in.
    val currentState by rememberUpdatedState(state)
    val currentOnChange by rememberUpdatedState(onChange)
    // While a row is dragged, the order is worked out here (one state object for the whole life of the sheet); it is saved when the finger lifts.
    var working by remember { mutableStateOf(state.order) }
    var dragging by remember { mutableStateOf<StatsView?>(null) }
    var offset by remember { mutableFloatStateOf(0f) }
    // A change made elsewhere (Move up, Move down, another saved order) shows here too, but never under a finger.
    LaunchedEffect(state.order) { if (dragging == null) working = state.order }

    AbSheet(onDismiss = onDismiss) {
        Text("Stats views", style = type.head, color = colors.ink)
        Text("Drag to reorder. Hidden views stay one tap away here.", style = type.bodyMedium, color = colors.muted)
        Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState())) {
            working.forEachIndexed { index, view ->
                // Keyed by the view, so a row that moves while it is dragged keeps its gesture.
                key(view) {
                    val hidden = view in state.hidden
                    val isDragged = dragging == view
                    Row(
                        Modifier
                            .fillMaxWidth()
                            .heightIn(min = ROW_HEIGHT)
                            .onSizeChanged { if (index == 0) rowPx = it.height.toFloat() }
                            .padding(vertical = 4.dp)
                            .zIndex(if (isDragged) 1f else 0f)
                            .graphicsLayer { translationY = if (isDragged) offset else 0f }
                            .semantics {
                                customActions = listOf(
                                    CustomAccessibilityAction("Move up") { if (index > 0) currentOnChange(currentState.copy(order = working.toMutableList().apply { add(index - 1, removeAt(index)) })); true },
                                    CustomAccessibilityAction("Move down") { if (index < working.lastIndex) currentOnChange(currentState.copy(order = working.toMutableList().apply { add(index + 1, removeAt(index)) })); true },
                                )
                            }
                            .clickable(role = Role.Button) { onOpen(view) },
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(4.dp),
                    ) {
                        Box(
                            Modifier
                                .size(44.dp)
                                .pointerInput(view) {
                                    detectDragGestures(
                                        onDragStart = { dragging = view; offset = 0f },
                                        onDragEnd = {
                                            dragging = null
                                            offset = 0f
                                            currentOnChange(currentState.copy(order = working))
                                        },
                                        onDragCancel = { dragging = null; offset = 0f; working = currentState.order },
                                    ) { change, drag ->
                                        change.consume()
                                        offset += drag.y
                                        val current = working.indexOf(view)
                                        val target = (current + (offset / rowPx).roundToInt()).coerceIn(0, working.lastIndex)
                                        if (target != current) {
                                            working = working.toMutableList().apply { add(target, removeAt(current)) }
                                            offset -= (target - current) * rowPx
                                        }
                                    }
                                },
                            contentAlignment = Alignment.Center,
                        ) {
                            Icon(Icons.Outlined.DragIndicator, contentDescription = "Drag ${view.title} to reorder", tint = colors.faint, modifier = Modifier.size(22.dp))
                        }

                        Icon(view.icon(), contentDescription = null, tint = if (hidden) colors.faint else colors.ink, modifier = Modifier.size(22.dp))
                        Column(Modifier.weight(1f).padding(start = 8.dp)) {
                            Text(view.title, style = type.body, fontWeight = FontWeight.Medium, color = if (hidden) colors.faint else colors.ink)
                            Text(view.about, style = type.small, color = if (hidden) colors.faint else colors.muted)
                        }

                        Box(
                            Modifier.size(44.dp).clickable(role = Role.Switch) { currentOnChange(currentState.toggle(view)) },
                            contentAlignment = Alignment.Center,
                        ) {
                            Icon(
                                if (hidden) Icons.Outlined.VisibilityOff else Icons.Outlined.Visibility,
                                contentDescription = if (hidden) "Show ${view.title}" else "Hide ${view.title}", tint = colors.muted, modifier = Modifier.size(22.dp),
                            )
                        }
                    }
                }
            }
        }
    }
}

private val ROW_HEIGHT = 60.dp
