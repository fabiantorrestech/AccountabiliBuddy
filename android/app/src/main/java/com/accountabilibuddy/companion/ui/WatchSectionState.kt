package com.accountabilibuddy.companion.ui

import com.accountabilibuddy.shared.WatchLinkSettings
import com.accountabilibuddy.shared.WatchNode

/** The Pixel Watch link (ADR 21) as Devices & sync shows it: the saved link and the watches the phone can see right now. */
data class WatchSectionState(val settings: WatchLinkSettings = WatchLinkSettings(), val detected: List<WatchNode> = emptyList())
