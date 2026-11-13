package com.accountabilibuddy.companion.ui.tasks.page

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.ContentCopy
import androidx.compose.material.icons.outlined.Devices
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.TaskStepLetters
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskRowText
import java.time.LocalDate
import java.time.ZoneId

/**
 * A task opened in place under its row (bundle spec §4 C, in R7.3a's reading form): "Steps · 1/3" and the steps with their letters
 * (a tap ticks one), the note, the reminders line, how it syncs and the ID chip ("142 ⧉", a tap copies it). Changing the label, time
 * limit, tags and the rest is the ⋮ sheet's; adding and dragging steps comes with R7.3b.
 */
@Composable
fun OpenTask(task: TaskItem, self: String, zone: ZoneId, onTickStep: (String) -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val context = LocalContext.current
    val steps = task.steps.orEmpty()
    val note = task.note
    Column(modifier.fillMaxWidth().padding(start = 40.dp, end = 8.dp, bottom = 10.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        if (steps.isNotEmpty()) {
            Text("Steps · ${TaskRowText.steps(task)}", style = Ab.type.small, fontWeight = FontWeight.SemiBold, color = colors.muted)
            steps.forEachIndexed { index, step ->
                Row(
                    Modifier.fillMaxWidth().heightIn(min = 44.dp).clip(RoundedCornerShape(12.dp))
                        .clickable(role = Role.Checkbox, onClickLabel = if (step.isDone) "Untick" else "Tick") { onTickStep(step.id) },
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    Box(
                        Modifier.size(20.dp).then(if (step.isDone) Modifier.background(colors.met, CircleShape) else Modifier.border(2.dp, colors.faint, CircleShape)),
                        contentAlignment = Alignment.Center,
                    ) {
                        if (step.isDone) Icon(Icons.Outlined.Check, contentDescription = null, tint = colors.bg, modifier = Modifier.size(13.dp))
                    }
                    Text(TaskStepLetters.of(index), style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted)
                    Text(
                        step.title, style = Ab.type.bodyMedium.copy(fontSize = 14.5.sp), color = if (step.isDone) colors.muted else colors.ink,
                        textDecoration = if (step.isDone) TextDecoration.LineThrough else null, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f),
                    )
                    val limit = step.estimateMinutes
                    if (limit != null) {
                        Icon(Icons.Outlined.Timer, contentDescription = null, tint = colors.muted, modifier = Modifier.size(14.dp))
                        Text(TaskRowText.minutes(limit), style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted)
                    }
                }
            }
        }
        if (!note.isNullOrBlank()) {
            Text(
                note, style = Ab.type.bodyMedium.copy(fontSize = 14.sp), color = colors.ink,
                modifier = Modifier.fillMaxWidth().background(colors.surface2, RoundedCornerShape(14.dp)).padding(12.dp),
            )
        }
        TaskRowText.reminder(task, LocalDate.now(zone), zone)?.let { reminder ->
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Icon(Icons.Outlined.Notifications, contentDescription = "Reminders", tint = colors.muted, modifier = Modifier.size(16.dp))
                Text(reminder, style = Ab.type.small, color = colors.muted)
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Icon(Icons.Outlined.Devices, contentDescription = null, tint = colors.faint, modifier = Modifier.size(16.dp))
            Text("Syncs to: ${TaskRowText.devices(task, self).replaceFirstChar { it.lowercase() }}", style = Ab.type.small, color = colors.faint, maxLines = 1, modifier = Modifier.weight(1f))
            val id = TaskRowText.id(task)
            if (id != null) {
                Row(
                    Modifier.clip(CircleShape).background(colors.surface2).clickable(role = Role.Button) { copyTaskId(context, id) }
                        .semantics { contentDescription = "Copy ID $id" }.padding(horizontal = 10.dp, vertical = 5.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                ) {
                    Text(id, style = Ab.type.small.copy(fontFeatureSettings = "tnum"), color = colors.muted)
                    Icon(Icons.Outlined.ContentCopy, contentDescription = null, tint = colors.muted, modifier = Modifier.size(13.dp))
                }
            } else {
                Spacer(Modifier.size(1.dp))
            }
        }
    }
}

/** Puts a task's number on the clipboard (the number only: never a title). */
fun copyTaskId(context: Context, id: String) {
    context.getSystemService(ClipboardManager::class.java)?.setPrimaryClip(ClipData.newPlainText("Task ID", id))
}
