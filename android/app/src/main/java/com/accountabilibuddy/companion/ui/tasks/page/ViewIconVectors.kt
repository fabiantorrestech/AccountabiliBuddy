package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.BookmarkBorder
import androidx.compose.material.icons.outlined.Bolt
import androidx.compose.material.icons.outlined.CalendarToday
import androidx.compose.material.icons.outlined.Eco
import androidx.compose.material.icons.outlined.Home
import androidx.compose.material.icons.outlined.LocalFireDepartment
import androidx.compose.material.icons.outlined.Psychology
import androidx.compose.material.icons.outlined.School
import androidx.compose.material.icons.outlined.Star
import androidx.compose.material.icons.outlined.StarBorder
import androidx.compose.material.icons.outlined.Work
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.TaskViewIcons

/** A saved view's icon as the phone draws it: the PC's Material name mapped to the same meaning here; the bookmark for none or an unknown one. */
object ViewIconVectors {
    fun of(name: String?): ImageVector = when (TaskViewIcons.drawn(name)) {
        "star-outline" -> Icons.Outlined.StarBorder
        "star" -> Icons.Outlined.Star
        "lightning-bolt" -> Icons.Outlined.Bolt
        "head-cog" -> Icons.Outlined.Psychology
        "briefcase-outline" -> Icons.Outlined.Work
        "home-outline" -> Icons.Outlined.Home
        "calendar-today" -> Icons.Outlined.CalendarToday
        "fire" -> Icons.Outlined.LocalFireDepartment
        "leaf" -> Icons.Outlined.Eco
        "school-outline" -> Icons.Outlined.School
        else -> Icons.Outlined.BookmarkBorder
    }

    /** The words a screen reader says for an icon choice ("lightning-bolt" as "lightning bolt"). */
    fun spoken(name: String): String = name.removeSuffix("-outline").replace('-', ' ')
}

/** The picker for a view's icon: the PC's ten, a round 44 dp target each, the picked one raised. [picked] null shows the bookmark picked. */
@Composable
fun ViewIconPicker(picked: String?, onPick: (String) -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val current = TaskViewIcons.picked(picked)
    FlowRow(modifier.padding(vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        TaskViewIcons.NAMES.forEach { name ->
            val selected = name == current
            Surface(
                onClick = { onPick(name) },
                modifier = Modifier.size(44.dp).semantics { this.selected = selected; role = Role.RadioButton; contentDescription = ViewIconVectors.spoken(name) },
                shape = Ab.shapes.pill,
                color = if (selected) colors.surface else colors.surface2,
                contentColor = if (selected) colors.ink else colors.muted,
                border = if (selected) BorderStroke(1.5.dp, colors.accent) else null,
            ) {
                androidx.compose.foundation.layout.Box(contentAlignment = Alignment.Center) {
                    Icon(ViewIconVectors.of(name), contentDescription = null, modifier = Modifier.size(22.dp))
                }
            }
        }
    }
}
