package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.SyncBoardWords
import com.accountabilibuddy.shared.tasks.TaskConflict
import com.accountabilibuddy.shared.tasks.TaskConflictPick
import com.accountabilibuddy.shared.tasks.TaskConflictPicker
import com.accountabilibuddy.shared.tasks.TaskConflictVersion
import com.accountabilibuddy.shared.tasks.TaskLists
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset

/**
 * "Changed on both devices" on the phone (SYNC1c, spec 05 section 11; the PC's ConflictDialog): a task's title or note was changed on two devices
 * since they last agreed. Both versions are listed, the newer first and marked; a tap keeps that one (stamped so the conflict closes on every device
 * once synced), "Keep both as separate tasks" puts the other version in Queued, "Decide later" changes nothing. A task with two conflicts (title
 * and note) shows them one after the other. The answer is not undoable and offers no Undo (it would bring the old text back without the choice).
 * The text is the user's own: shown here only, never logged. The pick goes through [onAnswer] (the task store's stamped edit).
 */
@Composable
fun TaskConflictSheet(
    lists: TaskLists,
    taskId: String,
    deviceNameOf: (String?) -> String,
    onAnswer: (TaskConflict, TaskConflictPick) -> String?,
    onDismiss: () -> Unit,
) {
    var picked by remember { mutableIntStateOf(0) }
    var toast by remember { mutableStateOf<String?>(null) }
    val view = TaskConflictPicker.view(lists, taskId, picked)
    // The task left the list or the conflict closed (answered on another device, or the last pick here): the sheet goes.
    LaunchedEffect(view == null) {
        if (view == null) onDismiss()
    }

    if (view == null) return
    val colors = Ab.colors
    val zone = ZoneId.systemDefault()
    val now = OffsetDateTime.now(ZoneOffset.UTC)
    AbSheet(onDismiss) {
        Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text(view.head, style = Ab.type.head, color = colors.ink)
            if (view.countText.isNotEmpty()) Text(view.countText, style = Ab.type.small, color = colors.muted)
            Text(view.taskTitle.ifBlank { "(untitled task)" }, style = Ab.type.small, color = colors.muted, maxLines = 2)
            view.versions.forEach { version ->
                VersionCard(version, deviceNameOf, now, zone, onKeep = {
                    toast = onAnswer(view.conflict, if (version.shown) TaskConflictPick.KEEP_THIS else TaskConflictPick.KEEP_OTHER)
                    picked++
                })
            }

            AbTonalButton("Keep both as separate tasks", onClick = {
                toast = onAnswer(view.conflict, TaskConflictPick.KEEP_BOTH)
                picked++
            }, modifier = Modifier.fillMaxWidth())
            AbTextButton("Decide later", onClick = onDismiss, modifier = Modifier.fillMaxWidth())
            Text("Steps, tags and reminders changed on only one device are kept either way.", style = Ab.type.small, color = colors.muted)
            toast?.let { Text(it, style = Ab.type.small, color = colors.muted) }
        }
    }
}

@Composable
private fun VersionCard(version: TaskConflictVersion, deviceNameOf: (String?) -> String, now: OffsetDateTime, zone: ZoneId, onKeep: () -> Unit) {
    val colors = Ab.colors
    val empty = version.text.isNullOrBlank()
    Surface(
        Modifier.fillMaxWidth(),
        shape = Ab.shapes.mediumShape,
        color = colors.surface2,
        contentColor = colors.ink,
        border = if (version.isNewer) BorderStroke(2.dp, colors.ink) else BorderStroke(1.dp, colors.line),
    ) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(if (empty) "(no note)" else version.text!!.trim(), style = Ab.type.bodyMedium, color = colors.ink)
            val whose = if (version.deviceId == null) "An earlier copy" else deviceNameOf(version.deviceId)
            val edited = "$whose · ${SyncBoardWords.clock(version.at, now, zone)}"
            Text(if (version.isNewer) "$edited · newer" else edited, style = Ab.type.small, color = if (version.isNewer) colors.ink else colors.muted, fontWeight = if (version.isNewer) FontWeight.SemiBold else FontWeight.Normal)
            AbTonalButton("Keep this one", onClick = onKeep, modifier = Modifier.fillMaxWidth())
        }
    }
}
