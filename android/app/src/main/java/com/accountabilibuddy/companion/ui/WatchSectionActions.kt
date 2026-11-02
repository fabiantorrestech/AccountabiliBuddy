package com.accountabilibuddy.companion.ui

import com.accountabilibuddy.shared.WatchNode

/** What the Pixel Watch section can ask for. */
data class WatchSectionActions(
    val onFind: () -> Unit = {},
    val onLink: (WatchNode) -> Unit = {},
    val onUnlink: () -> Unit = {},
    val onDataLayer: (Boolean) -> Unit = {},
    val onRelay: (Boolean) -> Unit = {},
)
