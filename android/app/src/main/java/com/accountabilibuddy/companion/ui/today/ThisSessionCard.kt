package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.sync.RunningTasks
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbCheck
import com.accountabilibuddy.companion.ui.tasks.parseCategoryColor
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.SessionTaskItem

/**
 * This session on the phone's Today (brief §4.1, board B-Phone-Session): numbered rows, the current item bold and first with its
 * sub-line ("↳ <its task> · ● <category>" for a step, "● <category>" for a task), then the session's other open tasks in the PC's
 * order; the tasks already ticked follow as a faded, folded "Finished" section. Ticks go to the PC as before ([TaskTicker]).
 *
 * The phone still reads the PC's session checklist (the old task model until R7): it has no task IDs, time estimates, Queued or
 * Reminders due today to show, so those parts of the board are not drawn (HANDOFF-QUESTIONS Round 2).
 */
@Composable
fun ThisSessionCard(running: RunningTasks) {
    val ticker = rememberTaskTicker(running)
    val current = CurrentItem.of(running)
    val others = SessionTaskItem.openInShownOrder(running.tasks).filter { it !== current?.task }
    val finished = running.tasks.filter { it.isDone }
    var showFinished by rememberSaveable { mutableStateOf(false) }
    Column(Modifier.fillMaxWidth()) {
        AbCard(contentPadding = PaddingValues(horizontal = 12.dp, vertical = 6.dp), verticalSpacing = 0.dp) {
            var number = 0
            if (current != null) {
                number++
                TaskLine(
                    number = number, item = current.item, ticker = ticker, isCurrent = true,
                    subLine = { SubLine(parent = current.task.title.takeIf { current.isStep }, current.task) },
                )
            }
            others.forEach { task ->
                if (number > 0) HorizontalDivider(color = Ab.colors.line, thickness = 1.dp, modifier = Modifier.padding(start = 40.dp))
                number++
                TaskLine(number = number, item = task, ticker = ticker, isCurrent = false, subLine = { SubLine(parent = null, task) })
            }
            if (current == null && others.isEmpty()) {
                Text("All tasks done.", style = Ab.type.bodyMedium, color = Ab.colors.muted, modifier = Modifier.padding(vertical = 14.dp, horizontal = 8.dp))
            }
        }
        if (finished.isNotEmpty()) {
            FoldHeader("Finished", finished.size, showFinished) { showFinished = !showFinished }
            if (showFinished) {
                Column(Modifier.alpha(0.6f)) {
                    finished.forEach { task -> TaskLine(number = null, item = task, ticker = ticker, isCurrent = false, subLine = {}) }
                }
            }
        }
    }
}

// One numbered row: number, tick box, title (bold for the current item), and its sub-line. A finished row has no number.
@Composable
private fun TaskLine(number: Int?, item: SessionTaskItem, ticker: TaskTicker, isCurrent: Boolean, subLine: @Composable () -> Unit) {
    val colors = Ab.colors
    val done = item.isDone || ticker.sending(item.id)
    val canTick = !done && ticker.canTick && item.id != null
    Row(
        Modifier.fillMaxWidth().heightIn(min = 52.dp)
            .toggleable(value = done, enabled = canTick, role = Role.Checkbox, onValueChange = { ticker.tick(item.id) })
            .semantics { stateDescription = if (done) "Done" else if (isCurrent) "Current" else "Open" }
            .padding(vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.widthIn(min = 28.dp), contentAlignment = Alignment.Center) {
            if (number != null) {
                Text(
                    number.toString(), style = Ab.type.bodyMedium.copy(fontFeatureSettings = "tnum"),
                    fontWeight = if (isCurrent) FontWeight.Bold else FontWeight.Normal,
                    color = if (isCurrent) colors.accent else colors.muted,
                )
            }
        }
        Spacer(Modifier.width(8.dp))
        AbCheck(done)
        Spacer(Modifier.width(14.dp))
        Column(Modifier.weight(1f)) {
            Text(
                item.title, style = Ab.type.body, fontWeight = if (isCurrent) FontWeight.SemiBold else FontWeight.Normal,
                color = if (done) colors.muted else colors.ink, textDecoration = if (done) TextDecoration.LineThrough else null,
                maxLines = 3, overflow = TextOverflow.Ellipsis,
            )
            subLine()
        }
    }
}

// "↳ <task> · ● <category>": the parent line only for a step; the category with its dot when the task has one.
@Composable
private fun SubLine(parent: String?, task: SessionTaskItem) {
    val category = task.category
    if (parent == null && category == null) return
    Row(verticalAlignment = Alignment.CenterVertically) {
        if (parent != null) {
            Text("↳ $parent", style = Ab.type.small, color = Ab.colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
            if (category != null) Text(" · ", style = Ab.type.small, color = Ab.colors.muted)
        }
        if (category != null) {
            Box(Modifier.size(7.dp).background(parseCategoryColor(task.categoryColor) ?: Ab.colors.faint, CircleShape))
            Spacer(Modifier.width(5.dp))
            Text(category, style = Ab.type.small, color = Ab.colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

/** A faded, folded section header under the list: "⌄ Finished 2"; tapping opens or closes it. */
@Composable
internal fun FoldHeader(title: String, count: Int, open: Boolean, onToggle: () -> Unit) {
    Row(
        Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable(role = Role.Button, onClick = onToggle)
            .semantics(mergeDescendants = true) { contentDescription = "$title, $count, ${if (open) "open" else "folded"}" }
            .padding(horizontal = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Icon(if (open) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore, contentDescription = null, tint = Ab.colors.faint, modifier = Modifier.size(20.dp))
        Text(title, style = Ab.type.bodyMedium, color = Ab.colors.faint)
        Text(count.toString(), style = Ab.type.bodyMedium.copy(fontFeatureSettings = "tnum"), color = Ab.colors.faint)
    }
}
