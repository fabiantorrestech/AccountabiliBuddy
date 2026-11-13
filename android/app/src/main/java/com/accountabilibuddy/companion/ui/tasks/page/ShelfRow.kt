package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.tween
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.MoreVert
import androidx.compose.material.icons.outlined.Star
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.LocalViewConfiguration
import androidx.compose.ui.platform.ViewConfiguration
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskRowPart
import kotlinx.coroutines.launch
import kotlin.math.abs
import kotlin.math.roundToInt

/** How long a hold on a task's title takes to open Rename (bundle spec §1.3). */
private const val TITLE_HOLD_MS = 550L

/** How far a row has to be swiped before letting go does its action (dp). */
private const val SWIPE_COMMIT_DP = 84

/**
 * One task row of a Shelf page (board Tasks-Phone): a 24 dp square checkbox, the title at 16 sp / 500 on up to two lines (one when
 * [compact]) with a gold ★ before a highlighted one and a faint gold tint on its row, the detail line at 13 sp ([parts], led by the
 * label's 8 dp dot when [showDot]; "Overdue 3 d" as an amber pill), and ⋮. At least 64 dp tall; no divider (brief §3.5).
 *
 * A tap opens the task in place ([expanded] draws [details] under it); holding the title for 550 ms asks to rename it ([onHoldTitle]);
 * holding the row elsewhere is [onHold] (the menu now; R7.3b lifts the row for a drag there). While its menu is open ([menuOpen]) the
 * row has an accent outline (brief §3.6). [swipe] gives the row's own swipes; an open row doesn't swipe.
 */
@OptIn(ExperimentalFoundationApi::class, ExperimentalLayoutApi::class)
@Composable
fun ShelfRow(
    task: TaskItem,
    parts: List<TaskRowPart>,
    labelColor: String?,
    showDot: Boolean,
    compact: Boolean,
    expanded: Boolean,
    menuOpen: Boolean,
    swipe: RowSwipe?,
    onTick: () -> Unit,
    onTap: () -> Unit,
    onMenu: () -> Unit,
    onHoldTitle: () -> Unit,
    onHold: () -> Unit,
    modifier: Modifier = Modifier,
    details: @Composable () -> Unit = {},
) {
    val colors = Ab.colors
    val density = LocalDensity.current
    val haptics = LocalHapticFeedback.current
    val scope = rememberCoroutineScope()
    val offset = remember(task.id) { Animatable(0f) }
    val commitPx = with(density) { SWIPE_COMMIT_DP.dp.toPx() }
    val currentSwipe = rememberUpdatedState(swipe)
    val shape = RoundedCornerShape(14.dp)
    val swipes = swipe != null && !expanded

    Box(modifier.fillMaxWidth()) {
        if (swipes && offset.value != 0f) SwipeBackdrop(swipe, toRight = offset.value > 0f, armed = abs(offset.value) >= commitPx)
        Column(
            Modifier
                .fillMaxWidth()
                .offset { IntOffset(offset.value.roundToInt(), 0) }
                .clip(shape)
                .background(if (offset.value != 0f) colors.surface else Color.Transparent)
                .background(if (task.highlight) colors.amberInk.copy(alpha = 0.10f) else Color.Transparent)
                .then(if (menuOpen) Modifier.border(1.5.dp, colors.accent, shape) else Modifier)
                // A sideways drag that starts on a row is always the row's: it never reaches the pager, so it never changes pages. A row
                // without swipes of its own (open, or finished) takes the drag and does nothing with it.
                .pointerInput(task.id, swipes) {
                    var armed = false
                    var drag = 0f
                    detectHorizontalDragGestures(
                        onDragStart = { armed = false; drag = 0f },
                        onDragEnd = {
                            val by = drag
                            scope.launch { offset.animateTo(0f, tween(170)) }
                            if (swipes && abs(by) >= commitPx) currentSwipe.value?.let { if (by > 0) it.onRight() else it.onLeft() }
                        },
                        onDragCancel = { scope.launch { offset.animateTo(0f, tween(170)) } },
                    ) { change, amount ->
                        change.consume()
                        if (!swipes) return@detectHorizontalDragGestures
                        drag += amount
                        val next = drag
                        scope.launch { offset.snapTo(next) }
                        val nowArmed = abs(next) >= commitPx
                        if (nowArmed && !armed) haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                        armed = nowArmed
                    }
                },
        ) {
            Row(
                Modifier
                    .fillMaxWidth()
                    .heightIn(min = if (compact) 48.dp else 64.dp)
                    .combinedClickable(onClick = onTap, onLongClick = onHold, onClickLabel = if (expanded) "Close" else "Open")
                    .padding(horizontal = 4.dp, vertical = if (compact) 6.dp else 10.dp),
                verticalAlignment = Alignment.Top,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                TickBox(task.isDone, onTick, Modifier.padding(top = 2.dp))
                Column(Modifier.weight(1f)) {
                    Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.Top) {
                        if (task.highlight) {
                            Icon(Icons.Outlined.Star, contentDescription = "Highlighted", tint = colors.amberInk, modifier = Modifier.padding(top = 2.dp).size(17.dp))
                        }
                        // The title's own hold is longer than the row's (550 ms), and opens Rename.
                        val system = LocalViewConfiguration.current
                        val titleHold = remember(system) {
                            object : ViewConfiguration by system {
                                override val longPressTimeoutMillis: Long get() = TITLE_HOLD_MS
                            }
                        }
                        CompositionLocalProvider(LocalViewConfiguration provides titleHold) {
                            Text(
                                task.title,
                                style = Ab.type.body.copy(fontSize = 16.sp, lineHeight = 21.sp), fontWeight = FontWeight.Medium, color = colors.ink,
                                maxLines = if (compact) 1 else 2, overflow = TextOverflow.Ellipsis,
                                modifier = Modifier.weight(1f).combinedClickable(onClick = onTap, onLongClick = onHoldTitle, onLongClickLabel = "Rename"),
                            )
                        }
                    }
                    if (parts.isNotEmpty() || showDot) {
                        FlowRow(
                            Modifier.padding(top = 2.dp),
                            horizontalArrangement = Arrangement.spacedBy(6.dp),
                            verticalArrangement = Arrangement.spacedBy(2.dp),
                            maxLines = if (compact) 1 else 3,
                        ) {
                            if (showDot) LabelDot(labelColor, 8.dp, Modifier.align(Alignment.CenterVertically))
                            parts.forEachIndexed { index, part ->
                                val text = if (index == parts.lastIndex) part.text else part.text + " ·"
                                if (part.late) {
                                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                        LatePill(part.text)
                                        if (index != parts.lastIndex) Text("·", style = Ab.type.small, color = colors.muted)
                                    }
                                } else {
                                    Text(text, style = Ab.type.small, color = colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                }
                            }
                        }
                    }
                }
                Box(
                    Modifier.size(36.dp).clip(CircleShape).clickable(role = Role.Button, onClickLabel = "Task menu", onClick = onMenu).semantics { contentDescription = "Task menu" },
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(Icons.Outlined.MoreVert, contentDescription = null, tint = colors.muted, modifier = Modifier.size(22.dp))
                }
            }
            if (expanded) details()
        }
    }
}

/** The 24 dp square checkbox of a row (radius 7, a 2 dp faint border; filled with a ✓ when ticked), in a 44 dp touch target. */
@Composable
fun TickBox(checked: Boolean, onTick: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val shape = RoundedCornerShape(7.dp)
    Box(
        modifier.size(24.dp).clip(shape)
            .then(if (checked) Modifier.background(colors.met, shape) else Modifier.border(2.dp, colors.faint, shape))
            .clickable(role = Role.Checkbox, onClick = onTick)
            .semantics { contentDescription = if (checked) "Finished. Tap to undo" else "Mark finished" },
        contentAlignment = Alignment.Center,
    ) {
        if (checked) Icon(Icons.Outlined.Check, contentDescription = null, tint = colors.bg, modifier = Modifier.size(16.dp))
    }
}

/** "Overdue 3 d": the amber pill for late and behind (brief §3.8). */
@Composable
fun LatePill(text: String) {
    val colors = Ab.colors
    Text(
        text, style = Ab.type.small.copy(fontSize = 12.sp), fontWeight = FontWeight.SemiBold, color = colors.amberInk, maxLines = 1,
        modifier = Modifier.background(colors.amberFill, CircleShape).border(1.dp, colors.amberLine, CircleShape).padding(horizontal = 7.dp, vertical = 1.dp),
    )
}

// What shows behind a row while it is swiped: where it goes, on the side it comes from.
@Composable
private fun SwipeBackdrop(swipe: RowSwipe, toRight: Boolean, armed: Boolean) {
    val colors = Ab.colors
    Row(
        Modifier.fillMaxWidth().heightIn(min = 48.dp).padding(horizontal = 14.dp, vertical = 14.dp),
        horizontalArrangement = if (toRight) Arrangement.Start else Arrangement.End,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        val tint = if (armed) colors.accent else colors.muted
        if (toRight) Icon(swipe.rightIcon, contentDescription = null, tint = tint, modifier = Modifier.size(20.dp))
        Text(
            if (toRight) swipe.rightLabel else swipe.leftLabel,
            style = Ab.type.small, fontWeight = FontWeight.SemiBold, color = tint, modifier = Modifier.padding(horizontal = 8.dp),
        )
        if (!toRight) Icon(swipe.leftIcon, contentDescription = null, tint = tint, modifier = Modifier.size(20.dp))
    }
}
