package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbPageHeader

/**
 * Tasks: the running session's checklist (ADR 24), the next session's tasks and the PC's open come-backs (ADR 30). Moved off
 * Today by the five-tab shell; the real Tasks page comes in a later part.
 */
@Composable
fun TasksScreen(
    /** The running session's checklist (ADR 24), from a sync file; null hides the card. */
    runningTasks: com.accountabilibuddy.companion.sync.RunningTasks? = null,
    /** The PC's open come-backs (ADR 30); null hides the card. */
    comebacks: com.accountabilibuddy.companion.sync.OpenComebacks? = null,
) {
    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item { AbPageHeader("Tasks") }
        runningTasks?.let { tasks ->
            item { TasksCard(tasks) }
        }
        item { NextTasksCard() }
        comebacks?.let { list ->
            item { ComebacksCard(list) }
        }
    }
}
