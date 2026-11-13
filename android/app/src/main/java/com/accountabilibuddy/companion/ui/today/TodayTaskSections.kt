package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.tasks.page.OpenTask
import com.accountabilibuddy.companion.ui.tasks.page.ShelfController
import com.accountabilibuddy.companion.ui.tasks.page.ShelfRow
import com.accountabilibuddy.companion.ui.tasks.page.TaskMenuPage
import com.accountabilibuddy.companion.ui.tasks.page.TaskMenuSheet
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskRowText
import java.time.OffsetDateTime
import java.time.ZoneOffset

/**
 * Today's task sections on the phone (round 2 R7.3; change note §2: Today owns today's sections, Tasks is planning): **This
 * session** (the next session's tasks; left out while a PC's session runs, whose own card shows its checklist), then the quieter,
 * folded **Queued** and **Finished today** (66%, 40% in a session). Rows are the Tasks page's ([ShelfRow]): tick, tap to open, ⋮ for
 * the task sheet. A section with nothing in it isn't drawn.
 *
 * Without these the tasks a drop on the Tasks page's Today strip sends to Queued would have nowhere to show on the phone.
 */
@Composable
fun TodayTaskSections(inSession: Boolean, onMessage: (String) -> Unit, modifier: Modifier = Modifier) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val lists by app.taskModel.lists.collectAsState()
    val controller = remember(app) { ShelfController(app, sessionFull = null) { toast -> onMessage(toast.message) } }
    val zone = app.taskModel.zone()
    val self = remember { app.sync.deviceId.toString() }
    val now = DotNetTime.cut(OffsetDateTime.now(ZoneOffset.UTC))
    var queuedOpen by remember { mutableStateOf(false) }
    var doneOpen by remember { mutableStateOf(false) }
    var expanded by remember { mutableStateOf<String?>(null) }
    var menu by remember { mutableStateOf<Pair<String, TaskMenuPage>?>(null) }
    val labels = lists.labels.map { it.label }.filter { it.isLive }
    val quiet = if (inSession) 0.4f else 0.66f

    @Composable
    fun rows(tasks: List<TaskItem>) {
        tasks.forEach { task ->
            val label = labels.firstOrNull { it.id.equals(task.labelId, ignoreCase = true) }
            ShelfRow(
                task = task,
                parts = TaskRowText.detail(task, label?.name, now, zone, showBin = false),
                labelColor = label?.color, showDot = true, compact = false,
                expanded = expanded == task.id, swipe = null,
                onTick = { if (task.isDone) controller.reopen(task.id) else controller.finish(task.id) },
                onTap = { expanded = if (expanded == task.id) null else task.id },
                onMenu = { menu = task.id to TaskMenuPage.Main },
                // A hold anywhere on a Today row (its title too) opens its menu; the title of an open task renames it.
                onTitle = { menu = task.id to TaskMenuPage.Rename },
                onHold = { menu = task.id to TaskMenuPage.Main },
            ) {
                OpenTask(task, self, zone, onTickStep = { step -> controller.tickStep(task.id, step) })
            }
        }
    }

    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        val session = todayTasks(lists, TaskBin.SESSION)
        if (!inSession && session.isNotEmpty()) {
            Section("This session", session.size.toString(), open = true, onToggle = null, strength = 1f) { rows(session) }
        }
        val queued = todayTasks(lists, TaskBin.QUEUED)
        if (queued.isNotEmpty()) {
            Section("Queued", queued.size.toString(), open = queuedOpen, onToggle = { queuedOpen = !queuedOpen }, strength = quiet) { rows(queued) }
        }
        val done = todayTasks(lists, TaskBin.DONE)
        if (done.isNotEmpty()) {
            Section("Finished today", done.size.toString(), open = doneOpen, onToggle = { doneOpen = !doneOpen }, strength = quiet) { rows(done) }
        }
    }

    menu?.let { (id, start) ->
        val task = lists.tasks.firstOrNull { it.id == id && it.task.isLive }?.task
        if (task != null) TaskMenuSheet(task, lists, self, zone, controller, pc = null, onDismiss = { menu = null }, start = start)
    }
}

/** Whether Today has a task section to draw: a next-session task (not while a PC's session runs), a queued one or one finished today. */
fun hasTodayTasks(lists: TaskLists, inSession: Boolean): Boolean =
    lists.tasks.any { it.task.isLive && (it.task.bin == TaskBin.QUEUED || it.task.bin == TaskBin.DONE || (!inSession && it.task.bin == TaskBin.SESSION)) }

private fun todayTasks(lists: TaskLists, bin: TaskBin): List<TaskItem> =
    lists.tasks.map { it.task }.filter { it.isLive && it.bin == bin }.sortedWith(compareBy<TaskItem> { it.position }.thenBy { it.id })

// One section card: the header (a chevron when it folds, the name, the count) and its rows when open. Quieter sections are faded as a whole.
@Composable
private fun Section(title: String, count: String, open: Boolean, onToggle: (() -> Unit)?, strength: Float, rows: @Composable () -> Unit) {
    val colors = Ab.colors
    val secondary = onToggle != null
    Column(Modifier.fillMaxWidth().alpha(if (open && !secondary) 1f else strength).clip(Ab.shapes.cardShape).background(colors.surface).padding(horizontal = 10.dp, vertical = 6.dp)) {
        Row(
            Modifier.fillMaxWidth().heightIn(min = 40.dp).then(if (onToggle != null) Modifier.clickable(role = Role.Button, onClickLabel = if (open) "Fold" else "Unfold", onClick = onToggle) else Modifier)
                .padding(horizontal = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            if (secondary) Icon(if (open) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore, contentDescription = null, tint = colors.faint, modifier = Modifier.size(20.dp))
            Text(
                title, style = Ab.type.bodyMedium.copy(fontSize = if (secondary) 14.5.sp else 15.5.sp), fontWeight = FontWeight.Medium,
                color = if (secondary) colors.faint else colors.muted, modifier = Modifier.semantics { heading() },
            )
            Text(count, style = Ab.type.bodyMedium.copy(fontSize = 14.5.sp, fontFeatureSettings = "tnum"), color = colors.faint)
        }
        if (open) rows()
    }
}
