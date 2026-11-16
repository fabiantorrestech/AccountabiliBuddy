package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.ScrollState
import androidx.compose.foundation.gestures.ScrollableState
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.input.nestedscroll.NestedScrollConnection
import androidx.compose.ui.input.nestedscroll.NestedScrollSource
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.unit.Velocity
import androidx.compose.ui.unit.dp
import kotlin.math.abs
import kotlin.math.sign

/** Whether step scrolling is on (Settings › Appearance › Animations, [UiPrefs.stepScroll]); provided by the theme. */
val LocalStepScroll = staticCompositionLocalOf { false }

/**
 * Step scrolling for e-ink screens: while the finger moves, the list stays put and jumps a third of its own height each time
 * the finger has travelled that far, so the screen redraws once per step; when the finger lifts the rest of the distance is
 * one last jump, and a flick never glides on. Does nothing while the setting is off. Put it before the scrollable modifier
 * (or on a Lazy list's modifier) so it hears the drag first.
 */
fun Modifier.stepScroll(state: ScrollableState, vertical: Boolean = true): Modifier = composed {
    if (!LocalStepScroll.current) {
        Modifier
    } else {
        var extent by remember { mutableIntStateOf(0) }
        val connection = remember(state, vertical) { StepScrollConnection(state, vertical) }
        val minimumStep = with(androidx.compose.ui.platform.LocalDensity.current) { 48.dp.toPx() }
        connection.stepPx = maxOf(extent / 3f, minimumStep)
        Modifier.onSizeChanged { extent = if (vertical) it.height else it.width }.nestedScroll(connection)
    }
}

/** [verticalScroll] with [stepScroll] in front of it. */
fun Modifier.abVerticalScroll(state: ScrollState, enabled: Boolean = true, reverseScrolling: Boolean = false): Modifier =
    stepScroll(state).verticalScroll(state, enabled = enabled, reverseScrolling = reverseScrolling)

/** [horizontalScroll] with [stepScroll] in front of it. */
fun Modifier.abHorizontalScroll(state: ScrollState, enabled: Boolean = true, reverseScrolling: Boolean = false): Modifier =
    stepScroll(state, vertical = false).horizontalScroll(state, enabled = enabled, reverseScrolling = reverseScrolling)

/** [LazyColumn] that follows step scrolling ([stepScroll]); the parameters the app uses, with LazyColumn's defaults. */
@Composable
fun AbLazyColumn(
    modifier: Modifier = Modifier,
    state: LazyListState = rememberLazyListState(),
    contentPadding: PaddingValues = PaddingValues(0.dp),
    verticalArrangement: Arrangement.Vertical = Arrangement.Top,
    horizontalAlignment: Alignment.Horizontal = Alignment.Start,
    content: LazyListScope.() -> Unit,
) {
    LazyColumn(
        modifier.stepScroll(state),
        state = state,
        contentPadding = contentPadding,
        verticalArrangement = verticalArrangement,
        horizontalAlignment = horizontalAlignment,
        content = content,
    )
}

/**
 * Takes the whole drag on its axis and moves the list itself, in steps of [stepPx]. [ScrollableState.dispatchRawDelta] moves at
 * once (no animation, no nested scroll) and ignores the scrollable's reversed direction, hence the minus: a finger moving down
 * (positive y) shows what is above, which is a negative scroll.
 */
private class StepScrollConnection(private val state: ScrollableState, private val vertical: Boolean) : NestedScrollConnection {
    var stepPx = 300f
    private var pending = 0f

    override fun onPreScroll(available: Offset, source: NestedScrollSource): Offset {
        if (source != NestedScrollSource.UserInput) return Offset.Zero
        pending += if (vertical) available.y else available.x
        while (abs(pending) >= stepPx) {
            val step = sign(pending) * stepPx
            state.dispatchRawDelta(-step)
            pending -= step
        }
        return if (vertical) Offset(0f, available.y) else Offset(available.x, 0f)
    }

    override suspend fun onPreFling(available: Velocity): Velocity {
        flush()
        return if (vertical) Velocity(0f, available.y) else Velocity(available.x, 0f)
    }

    override suspend fun onPostFling(consumed: Velocity, available: Velocity): Velocity {
        flush()
        return Velocity.Zero
    }

    private fun flush() {
        if (pending != 0f) {
            state.dispatchRawDelta(-pending)
            pending = 0f
        }
    }
}
