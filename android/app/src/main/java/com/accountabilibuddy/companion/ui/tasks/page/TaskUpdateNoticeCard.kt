package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Info
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.companion.tasks.TasksUiPrefs
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.tasks.TaskListUpdateNotice
import kotlinx.coroutines.delay

/** The "Update AccountabiliBuddy on" notice as a screen draws it: the devices' [names] and what × does. */
class TaskUpdateNotice(val names: List<String>, val onDismiss: () -> Unit)

/**
 * Whether the "Update AccountabiliBuddy on" notice shows now, for the Tasks page and the old Tasks pane alike
 * ([TaskListUpdateNotice]): the PCs among the received [files] that share their lists without the one task list, unless the notice
 * was closed for exactly those devices less than 3 days ago. It is looked at again every minute, by the trusted clock
 * ([TrustedTime]), so it comes back by itself; what was closed, and when, is kept on this phone only ([TasksUiPrefs]). Null: no
 * notice (also while this phone keeps its task lists to itself: [shares] false).
 */
@Composable
fun rememberTaskUpdateNotice(files: List<SyncFile>, shares: Boolean): TaskUpdateNotice? {
    val prefs = TasksUiPrefs.of(LocalContext.current)
    val state by prefs.state.collectAsState()
    val now by produceState(TrustedTime.now()) {
        while (true) {
            delay(60_000)
            value = TrustedTime.now()
        }
    }
    val behind = remember(files) { TaskListUpdateNotice.pcsBehind(files) }
    val ids = behind.map { it.deviceId.toString() }
    // A close stamped after "now" (the clock went back) is pulled back, so it never hides the notice for longer than the 3 days.
    val stamped = state.noticeDismissedAt
    LaunchedEffect(stamped, now) {
        if (stamped != null && stamped.isAfter(now)) prefs.update { it.copy(noticeDismissedAt = TaskListUpdateNotice.dismissedAt(stamped, now)) }
    }
    if (!shares || !TaskListUpdateNotice.shows(ids, state.noticeDismissedFor, stamped, now)) return null
    return TaskUpdateNotice(behind.map { it.name }.distinct()) {
        prefs.update { it.copy(noticeDismissedAt = TrustedTime.now(), noticeDismissedFor = ids) }
    }
}

/**
 * The notice itself: "Update AccountabiliBuddy on:", each device on a line of its own, why, and an × that closes it (it comes back
 * after 3 days while a listed device is still behind, at once for a new one, and goes by itself when none is).
 */
@Composable
fun TaskUpdateNoticeCard(notice: TaskUpdateNotice, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Row(
        modifier.fillMaxWidth().clip(Ab.shapes.mediumShape).background(colors.infoBg).padding(start = 14.dp, top = 10.dp, bottom = 10.dp),
        verticalAlignment = Alignment.Top,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Icon(Icons.Outlined.Info, contentDescription = null, tint = colors.infoInk, modifier = Modifier.padding(top = 2.dp).size(18.dp))
        Column(Modifier.weight(1f)) {
            Text(TaskListUpdateNotice.HEADING, style = Ab.type.small, color = colors.infoInk)
            notice.names.forEach { name ->
                Text(name, style = Ab.type.small, fontWeight = FontWeight.SemiBold, color = colors.infoInk, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Text(TaskListUpdateNotice.why(notice.names.size), style = Ab.type.small, color = colors.infoInk)
        }
        Box(
            Modifier.size(44.dp).clip(CircleShape).clickable(role = Role.Button, onClick = notice.onDismiss).semantics { contentDescription = "Close this notice" },
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Outlined.Close, contentDescription = null, tint = colors.infoInk, modifier = Modifier.size(18.dp))
        }
    }
}
