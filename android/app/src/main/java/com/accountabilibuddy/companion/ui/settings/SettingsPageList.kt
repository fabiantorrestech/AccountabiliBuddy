package com.accountabilibuddy.companion.ui.settings

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

/** The key of the "Settings rest until ..." banner row a Guards list puts first while a focus session runs. */
const val REST_BANNER = "rest"

/**
 * A Settings page as a scrolling list of [rows] (no Scaffold, no fixed width). With an [anchor] (a row's key) the list opens
 * scrolled to that row, which is how a search result lands on its row.
 */
@Composable
fun SettingsPageList(rows: List<PageRow>, anchor: String?, modifier: Modifier = Modifier) {
    val state = rememberLazyListState()
    LazyColumn(modifier, state = state, contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        items(rows, key = { it.key }) { row -> row.content() }
    }
    ScrollToAnchor(state, anchor, rows.map { it.key })
}

/**
 * Scrolls [state] to the item at [anchor]'s place in [keys] (the keys of the list's items, in order) once when the page opens
 * with it; nothing when the anchor is null or not one of them. Memory only: nothing about the visit is kept.
 */
@Composable
fun ScrollToAnchor(state: LazyListState, anchor: String?, keys: List<String>) {
    LaunchedEffect(anchor) {
        var index = keys.indexOf(anchor).takeIf { anchor != null }
        // The "resting" banner right above a row stays in view with it.
        if (index != null && index > 0 && keys[index - 1] == REST_BANNER) index -= 1
        if (index != null && index >= 0) state.scrollToItem(index)
    }
}
