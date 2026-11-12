package com.accountabilibuddy.companion.ui.stats

import com.accountabilibuddy.companion.stats.StatsRange
import com.accountabilibuddy.companion.stats.StatsView
import com.accountabilibuddy.companion.stats.StatsViewsState
import java.util.UUID

/** What the Stats pane can ask its host to do. None of it starts, ends or unlocks anything. */
class StatsPaneActions(
    val onSelectView: (StatsView) -> Unit,
    val onSelectRange: (StatsRange) -> Unit,
    val onViewsChange: (StatsViewsState) -> Unit,
    /** "Show measured sleep": asks for the Health Connect read permission. */
    val onConnectHealth: () -> Unit,
    /** "Check again": reads the measured sleep again. */
    val onRefreshSleep: () -> Unit,
    /** "Allow usage access": opens Android's Usage access page. */
    val onAllowUsage: () -> Unit,
    /** The Goals view's chips: show another goal (round 2 R6b4). Held for the visit only. */
    val onPickGoal: (UUID) -> Unit = {},
)
