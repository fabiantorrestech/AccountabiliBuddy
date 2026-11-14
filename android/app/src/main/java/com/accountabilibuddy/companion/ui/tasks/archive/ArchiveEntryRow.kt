package com.accountabilibuddy.companion.ui.tasks.archive

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ChevronRight
import androidx.compose.material.icons.outlined.Inventory2
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/** The height of the Archive row at the bottom of the Tasks page; the round "+" sits above it. */
val ArchiveRowHeight = 56.dp

/**
 * The Archive row at the bottom of the Tasks page (AR1c): the PC's row ("Archive", its summary, a chevron) as a phone row. It opens the full Archive.
 * It reads nothing, so the Tasks page spends no time on it while the Archive is closed.
 */
@Composable
fun ArchiveEntryRow(onOpen: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Row(
        modifier.fillMaxWidth().height(ArchiveRowHeight).clickable(role = Role.Button, onClick = onOpen).padding(horizontal = 24.dp).semantics { contentDescription = "Archive" },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Icon(Icons.Outlined.Inventory2, contentDescription = null, tint = colors.muted, modifier = Modifier.size(22.dp))
        Text("Archive", style = Ab.type.bodyMedium, color = colors.muted)
        Text("Come-backs, notes, tasks done and left open", style = Ab.type.small, color = colors.faint, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
        Icon(Icons.Outlined.ChevronRight, contentDescription = null, tint = colors.muted, modifier = Modifier.size(24.dp))
    }
}
