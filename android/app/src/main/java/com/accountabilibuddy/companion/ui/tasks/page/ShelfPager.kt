package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearOutSlowInEasing
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.shared.tasks.ShelfPage
import com.accountabilibuddy.shared.tasks.ShelfPaging
import kotlinx.coroutines.launch
import kotlin.math.abs
import kotlin.math.roundToInt

/** How long a page change takes at normal speed (change note §2: about 150–180 ms, ease-out). */
private const val PAGE_CHANGE_MS = 170

/**
 * The Shelf, one page at a time (board Tasks-Phone, change note §2 and §4c): the card of the page [index] of [pages], slightly
 * narrower than the screen, with a slim peek of the neighbour on each side that has one ([PagePeek]; an end has none unless [wrap] is
 * on, and the space is kept so the card never changes width), and the page indicator under it.
 *
 * The page changes by a tap on a peek, a tap on a dot, or a swipe of about 40 dp that starts on the card's header or its empty space.
 * A swipe that starts on a row is the row's own and never reaches here (the row takes it first). Past an end with [wrap] off the card
 * gives a small rubber-band bounce. Every change is the same motion: a crossfade with a 14 dp slide in the direction of travel over
 * [PAGE_CHANGE_MS] × [motionScale] (ease-out); with motion off ([motionScale] 0) a plain fade.
 *
 * One page (Group by None) draws just the card, full width, with no peeks.
 */
@Composable
fun ShelfPager(
    pages: List<ShelfPage>,
    index: Int,
    wrap: Boolean,
    motionScale: Float,
    onIndex: (Int) -> Unit,
    modifier: Modifier = Modifier,
    card: @Composable (ShelfPage) -> Unit,
) {
    val density = LocalDensity.current
    val scope = rememberCoroutineScope()
    val count = pages.size
    val current = index.coerceIn(0, (count - 1).coerceAtLeast(0))
    val left = ShelfPaging.neighbour(current, count, -1, wrap)
    val right = ShelfPaging.neighbour(current, count, 1, wrap)
    // The direction of the change in flight: set by a peek or a swipe (so wrap around slides the way the finger went); a jump from
    // the dots or the toast goes by the indexes.
    val step = remember { intArrayOf(0) }
    val pull = remember { Animatable(0f) }
    val travelPx = with(density) { ShelfPaging.SWIPE_TRAVEL_DP.dp.toPx() }
    val slidePx = with(density) { 14.dp.toPx() }.roundToInt()
    val state = rememberUpdatedState(Triple(left, right, onIndex))

    fun go(to: Int?, by: Int) {
        if (to == null) {
            // An end with wrap around off: a small bounce, nothing changes.
            scope.launch {
                pull.snapTo(-by * slidePx.toFloat())
                pull.animateTo(0f, spring(dampingRatio = 0.55f, stiffness = 500f))
            }
            return
        }

        step[0] = by
        state.value.third(to)
    }

    Column(modifier.fillMaxSize()) {
        Row(
            Modifier.weight(1f).fillMaxWidth().padding(horizontal = if (count > 1) 6.dp else 16.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            if (count > 1) {
                if (left != null) PagePeek(pages[left], left = true, onClick = { go(left, -1) }) else Spacer(Modifier.width(PeekWidth))
            }
            Box(
                Modifier
                    .weight(1f)
                    .fillMaxHeight()
                    .graphicsLayer { translationX = pull.value }
                    .pointerInput(count) {
                        if (count <= 1) return@pointerInput
                        var total = 0f
                        detectHorizontalDragGestures(
                            onDragStart = { total = 0f },
                            onDragEnd = {
                                val (toLeft, toRight, _) = state.value
                                scope.launch { pull.animateTo(0f, tween(150)) }
                                // A swipe to the left shows the page on the right, like turning a page.
                                if (abs(total) >= travelPx) if (total < 0) go(toRight, 1) else go(toLeft, -1)
                            },
                            onDragCancel = { scope.launch { pull.animateTo(0f, tween(150)) } },
                        ) { change, amount ->
                            change.consume()
                            total += amount
                            val shown = (total * 0.25f).coerceIn(-slidePx * 2f, slidePx * 2f)
                            scope.launch { pull.snapTo(shown) }
                        }
                    },
            ) {
                AnimatedContent(
                    targetState = current,
                    transitionSpec = {
                        val direction = if (step[0] != 0) step[0] else if (targetState > initialState) 1 else -1
                        val time = (PAGE_CHANGE_MS * motionScale).roundToInt()
                        when {
                            // Motion off: a plain fade.
                            motionScale <= 0f -> fadeIn(tween(120)) togetherWith fadeOut(tween(120))
                            else -> (fadeIn(tween(time, easing = LinearOutSlowInEasing)) + slideInHorizontally(tween(time, easing = LinearOutSlowInEasing)) { direction * slidePx }) togetherWith
                                (fadeOut(tween(time, easing = LinearOutSlowInEasing)) + slideOutHorizontally(tween(time, easing = LinearOutSlowInEasing)) { -direction * slidePx })
                        }.also { step[0] = 0 }
                    },
                    label = "shelf page",
                    modifier = Modifier.fillMaxSize(),
                ) { shown ->
                    pages.getOrNull(shown)?.let { card(it) }
                }
            }
            if (count > 1) {
                if (right != null) PagePeek(pages[right], left = false, onClick = { go(right, 1) }) else Spacer(Modifier.width(PeekWidth))
            }
        }
        PageIndicator(pages.map { it.title }, current, onJump = { if (it != current) onIndex(it) })
    }
}
