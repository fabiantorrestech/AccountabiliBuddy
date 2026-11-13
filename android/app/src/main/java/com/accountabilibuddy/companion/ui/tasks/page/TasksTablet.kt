package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.runtime.Composable
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.shared.tasks.TasksTabletLayout

/** True while the Tasks tab draws for a tablet (R7.5): the phone layout at tablet sizes ([TasksTabletLayout]). */
val LocalTasksTablet = compositionLocalOf { false }

/** True when this device is a tablet for Tasks: its smallest width is 600 dp or more, in either orientation. */
@Composable
fun isTasksTablet(): Boolean = TasksTabletLayout.isTablet(LocalConfiguration.current.smallestScreenWidthDp)

/** The peek's width on this device (24 dp on a phone, 40 dp on a tablet); the room kept for a missing neighbour is the same. */
@Composable
fun peekWidth(): Dp = TasksTabletLayout.peekWidthDp(LocalTasksTablet.current).dp
