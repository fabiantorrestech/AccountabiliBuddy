package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.automirrored.outlined.DriveFileMove
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material.icons.outlined.Tune
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.ShelfDrag

/**
 * The header in select mode (board Tasks-Phone-Drag-Bin; bundle spec §12): × (stop selecting), "2 selected" with a ⌄ that opens
 * the selected list, and "Select all 4" (the rows of the page showing; "Unselect all" once they all are). It is drawn in the
 * page header's own place, at its size, so entering select mode moves nothing.
 */
@Composable
fun SelectHeader(count: Int, selectAllText: String, onStop: () -> Unit, onView: () -> Unit, onSelectAll: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Row(modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Box(
            Modifier.size(44.dp).clip(CircleShape).clickable(role = Role.Button, onClick = onStop).semantics { contentDescription = "Stop selecting" },
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Outlined.Close, contentDescription = null, tint = colors.ink, modifier = Modifier.size(22.dp))
        }
        Row(
            Modifier.heightIn(min = 44.dp).clip(CircleShape).clickable(role = Role.Button, onClickLabel = "Show the selected tasks", onClick = onView).padding(horizontal = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            Text(ShelfDrag.selectedText(count), style = Ab.type.body.copy(fontSize = 22.sp), fontWeight = FontWeight.SemiBold, color = colors.ink, maxLines = 1, modifier = Modifier.semantics { heading() })
            if (count > 0) Icon(Icons.Outlined.ExpandMore, contentDescription = null, tint = colors.muted, modifier = Modifier.size(20.dp))
        }
        Spacer(Modifier.weight(1f))
        Box(
            // 34 dp as drawn, inside a 48 dp touch target.
            Modifier.heightIn(min = 48.dp).clip(CircleShape).clickable(role = Role.Button, onClick = onSelectAll),
            contentAlignment = Alignment.Center,
        ) {
            Box(Modifier.height(34.dp).border(1.dp, colors.line, CircleShape).padding(horizontal = 14.dp), contentAlignment = Alignment.Center) {
                Text(selectAllText, style = Ab.type.small.copy(fontSize = 13.5.sp), fontWeight = FontWeight.SemiBold, color = colors.ink, maxLines = 1)
            }
        }
    }
}

/**
 * Select mode's bar at the foot of the page (bundle spec §12): **Move** · **Options** (Time limit, Label, Tags, Rename one by one)
 * · **Delete** with the count. These are the drag's moves and more, without dragging. Nothing is offered with nothing selected.
 */
@Composable
fun BulkBar(count: Int, onMove: () -> Unit, onOptions: () -> Unit, onDelete: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val shape = RoundedCornerShape(18.dp)
    Row(
        modifier.fillMaxWidth().shadow(12.dp, shape).clip(shape).background(colors.surface2).padding(horizontal = 6.dp, vertical = 4.dp),
        horizontalArrangement = Arrangement.spacedBy(4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        BulkAction(Icons.AutoMirrored.Outlined.DriveFileMove, "Move", colors.ink, count > 0, onMove, Modifier.weight(1f))
        BulkAction(Icons.Outlined.Tune, "Options", colors.ink, count > 0, onOptions, Modifier.weight(1f))
        BulkAction(Icons.Outlined.Delete, if (count > 0) "Delete $count" else "Delete", colors.drift, count > 0, onDelete, Modifier.weight(1f))
    }
}

@Composable
private fun BulkAction(icon: ImageVector, text: String, ink: Color, enabled: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier) {
    val tone = if (enabled) ink else ink.copy(alpha = 0.4f)
    Row(
        modifier.heightIn(min = 48.dp).clip(RoundedCornerShape(14.dp)).clickable(enabled = enabled, role = Role.Button, onClick = onClick).padding(horizontal = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.CenterHorizontally),
    ) {
        Icon(icon, contentDescription = null, tint = tone, modifier = Modifier.size(20.dp))
        Text(text, style = Ab.type.bodyMedium.copy(fontSize = 14.5.sp), fontWeight = FontWeight.SemiBold, color = tone, maxLines = 1)
    }
}
