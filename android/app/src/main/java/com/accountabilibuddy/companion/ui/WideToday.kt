package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.lazy.staggeredgrid.LazyVerticalStaggeredGrid
import androidx.compose.foundation.lazy.staggeredgrid.StaggeredGridCells
import androidx.compose.foundation.lazy.staggeredgrid.StaggeredGridItemSpan
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

/**
 * Today on a tablet: the same parts as the phone's column ([TodayPane] collects them), laid out as a dashboard. The date line, the
 * timeline (the day bar), the hero and the notes that must be read run across the top; the cards then fill two or three columns, each
 * going to the shortest column. Nothing here decides what shows: that is the person's Today layout, as on the phone.
 */
@Composable
fun WideToday(blocks: List<TodayBlock>, modifier: Modifier = Modifier) {
    BoxWithConstraints(modifier) {
        val columns = if (maxWidth >= 960.dp) 3 else 2
        LazyVerticalStaggeredGrid(
            columns = StaggeredGridCells.Fixed(columns),
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(start = 24.dp, end = 24.dp, top = 4.dp, bottom = 16.dp),
            horizontalArrangement = Arrangement.spacedBy(16.dp),
            verticalItemSpacing = 12.dp,
        ) {
            arranged(blocks).forEach { block ->
                item(key = block.key, span = if (isCard(block.key)) StaggeredGridItemSpan.SingleLane else StaggeredGridItemSpan.FullLine) {
                    block.content()
                }
            }
        }
    }
}

// The timeline goes right under the date line, whatever its place was in the phone's order.
private fun arranged(blocks: List<TodayBlock>): List<TodayBlock> {
    val timeline = blocks.firstOrNull { it.key == TIMELINE } ?: return blocks
    val rest = blocks - timeline
    val at = rest.indexOfFirst { it.key == "header" } + 1
    return rest.take(at) + timeline + rest.drop(at)
}

private const val TIMELINE = "more-daybar"

// The parts that are a card (one column); everything else (header, hero, banners, notes, the fold's toggle) runs across.
private val CARDS = setOf(
    "ring", "comebacks", "small", "noise", "steps", "pc-missing",
    "more-workuse", "more-lunch", "more-beeminder", "more-timers", "more-dayoff", "more-import", "more-test",
)

private fun isCard(key: String): Boolean = key in CARDS
