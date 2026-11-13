package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
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
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The one-time card after the task migration (MIG1, the PC's EarlierTasksCard): "N tasks from the earlier version are in To sort",
 * "Sort them now" and an × that closes it for good. Today and the Tasks page draw it; never during a session (the callers decide).
 */
@Composable
fun EarlierTasksCard(count: Int, onSort: () -> Unit, onDismiss: () -> Unit, modifier: Modifier = Modifier) {
    if (count <= 0) return
    val colors = Ab.colors
    Row(
        modifier.fillMaxWidth().clip(Ab.shapes.mediumShape).background(colors.infoBg).padding(start = 14.dp, top = 6.dp, bottom = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Icon(Icons.Outlined.Info, contentDescription = null, tint = colors.infoInk, modifier = Modifier.size(18.dp))
        Text(
            if (count == 1) "1 task from the earlier version is in To sort." else "$count tasks from the earlier version are in To sort.",
            style = Ab.type.small, color = colors.infoInk, modifier = Modifier.weight(1f),
        )
        TextButton(onClick = onSort) { Text("Sort them now", color = colors.infoInk) }
        Box(
            Modifier.size(44.dp).clip(CircleShape).clickable(role = Role.Button, onClick = onDismiss).semantics { contentDescription = "Close this notice" },
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Outlined.Close, contentDescription = null, tint = colors.infoInk, modifier = Modifier.size(18.dp))
        }
    }
}
