package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.runtime.Composable

/**
 * Where Today's parts are put: one keyed block after another. The phone's column ([LazyListScope.asTodaySink]) draws them in a single
 * list; the tablet collects them first and lays them out in columns ([WideToday]). The parts themselves do not know which.
 */
interface TodaySink {
    fun item(key: String, content: @Composable () -> Unit)
}

/** Today's parts into a phone's lazy column: each block is one list item, exactly as before the tablet layout. */
fun LazyListScope.asTodaySink(): TodaySink = object : TodaySink {
    override fun item(key: String, content: @Composable () -> Unit) {
        this@asTodaySink.item(key = key) { content() }
    }
}

/** One collected block of Today. */
class TodayBlock(val key: String, val content: @Composable () -> Unit)

/** Collects Today's parts in order, for the tablet layout to arrange. */
class TodayBlocks : TodaySink {
    val blocks = mutableListOf<TodayBlock>()

    override fun item(key: String, content: @Composable () -> Unit) {
        blocks += TodayBlock(key, content)
    }
}
