package com.accountabilibuddy.companion.ui.goals

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.goals.GoalTaskLink
import com.accountabilibuddy.shared.goals.GoalTaskLinks

/**
 * The goal links of the phone's task list (GS5b): which tasks carry which goal and which are finished. A goal's numbers are worked out again
 * only when a link, a title or a finish changed (the list is equal then), not on every task change. Empty while the task list is not the source.
 */
@Composable
fun rememberTaskLinks(): List<GoalTaskLink> {
    val app = LocalContext.current.applicationContext as CompanionApp
    val lists by app.taskModel.lists.collectAsStateWithLifecycle()
    return remember(lists) { GoalTaskLinks.from(lists) }
}
