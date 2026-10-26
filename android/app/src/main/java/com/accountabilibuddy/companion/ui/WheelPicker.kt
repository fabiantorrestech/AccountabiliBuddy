package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.snapping.rememberSnapFlingBehavior
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.filter
import java.time.LocalTime

/**
 * A vertical wheel, flip-clock style: the value in the middle band is the chosen one; it snaps to a value when a
 * fling ends. [selected] can change from outside (it scrolls there).
 */
@Composable
fun WheelPicker(
    items: List<String>,
    selected: Int,
    onSelected: (Int) -> Unit,
    modifier: Modifier = Modifier,
    width: Dp = 64.dp,
    rowHeight: Dp = 40.dp,
    visibleRows: Int = 3,
) {
    val state = rememberLazyListState(initialFirstVisibleItemIndex = selected.coerceIn(0, (items.size - 1).coerceAtLeast(0)))
    val rowPx = with(LocalDensity.current) { rowHeight.toPx() }
    // The wheel's settle callback outlives compositions: always call the newest one.
    val currentOnSelected by rememberUpdatedState(onSelected)
    val center by remember {
        derivedStateOf {
            (state.firstVisibleItemIndex + if (state.firstVisibleItemScrollOffset > rowPx / 2) 1 else 0).coerceIn(0, (items.size - 1).coerceAtLeast(0))
        }
    }
    LaunchedEffect(state, items.size) {
        snapshotFlow { state.isScrollInProgress }.distinctUntilChanged().filter { !it }.collect {
            state.animateScrollToItem(center)
            currentOnSelected(center)
        }
    }
    LaunchedEffect(selected) {
        if (!state.isScrollInProgress && selected != center && selected in items.indices) state.scrollToItem(selected)
    }

    val padding = visibleRows / 2
    Box(modifier.width(width).height(rowHeight * visibleRows), contentAlignment = Alignment.Center) {
        Box(
            Modifier.fillMaxWidth().height(rowHeight)
                .background(MaterialTheme.colorScheme.primary.copy(alpha = 0.14f), RoundedCornerShape(10.dp)),
        )
        LazyColumn(state = state, flingBehavior = rememberSnapFlingBehavior(state), horizontalAlignment = Alignment.CenterHorizontally) {
            spacers(padding, rowHeight)
            itemsIndexed(items) { index, label ->
                val distance = kotlin.math.abs(index - center)
                Box(Modifier.height(rowHeight), contentAlignment = Alignment.Center) {
                    Text(
                        label,
                        style = MaterialTheme.typography.titleLarge,
                        fontWeight = if (distance == 0) FontWeight.SemiBold else FontWeight.Normal,
                        modifier = Modifier.alpha(if (distance == 0) 1f else 0.4f),
                    )
                }
            }
            spacers(padding, rowHeight)
        }
    }
}

private fun androidx.compose.foundation.lazy.LazyListScope.spacers(count: Int, height: Dp) {
    items(count) { Box(Modifier.height(height)) }
}

/**
 * A time on three wheels: hour (12, 1 … 11, as on a clock, so 12 never sits next to 11 and AM/PM always means what it
 * says), minute (every [minuteStep]) and AM/PM; always 12-hour (project convention).
 */
@Composable
fun TimeWheel(time: LocalTime, onChange: (LocalTime) -> Unit, modifier: Modifier = Modifier, minuteStep: Int = 5) {
    val hours = remember { listOf("12") + (1..11).map { it.toString() } }
    val minutes = remember(minuteStep) { (0 until 60 step minuteStep).map { "%02d".format(it) } }
    val halves = remember { listOf("AM", "PM") }
    val hour12 = if (time.hour % 12 == 0) 12 else time.hour % 12
    val pm = time.hour >= 12
    fun set(h12: Int, minute: Int, isPm: Boolean) {
        val hour = (h12 % 12) + if (isPm) 12 else 0
        onChange(LocalTime.of(hour, minute))
    }
    Row(modifier, horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
        WheelPicker(hours, hour12 % 12, { set(if (it == 0) 12 else it, time.minute, pm) }, width = 56.dp)
        Text(":", style = MaterialTheme.typography.titleLarge)
        WheelPicker(minutes, (time.minute / minuteStep).coerceIn(0, minutes.size - 1), { set(hour12, it * minuteStep, pm) }, width = 56.dp)
        WheelPicker(halves, if (pm) 1 else 0, { set(hour12, time.minute, it == 1) }, width = 64.dp)
    }
}
