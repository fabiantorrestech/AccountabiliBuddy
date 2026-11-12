package com.accountabilibuddy.companion.ui.goals

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.hoverable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.LocalMotionScale
import com.accountabilibuddy.companion.ui.motionSpec
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalArea

/**
 * An area's header on the phone's Goals list (brief §3.4, §4.3; change note §4): the area's 20 dp icon, its name at 18 sp bold in
 * ink, a count pill and a ▾ caret. A tap folds or unfolds the area (remembered per area on this phone); holding it opens Edit area.
 * The ✎ shows only while a mouse hovers the header or it has keyboard focus (touch uses the hold, or Options › Areas); its slot is
 * always kept, so nothing moves when it shows.
 */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun GoalAreaHeader(area: GoalArea, count: Int, collapsed: Boolean, onToggle: () -> Unit, onEdit: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val header = remember { MutableInteractionSource() }
    val pencil = remember { MutableInteractionSource() }
    val hovered by header.collectIsHoveredAsState()
    val focused by header.collectIsFocusedAsState()
    val pencilFocused by pencil.collectIsFocusedAsState()
    val pencilHovered by pencil.collectIsHoveredAsState()
    val caret by animateFloatAsState(if (collapsed) -90f else 0f, motionSpec(LocalMotionScale.current, 180), label = "caret")
    Row(
        modifier
            .fillMaxWidth()
            .heightIn(min = 52.dp)
            .hoverable(header)
            .combinedClickable(
                interactionSource = header,
                indication = androidx.compose.foundation.LocalIndication.current,
                role = Role.Button,
                onClickLabel = if (collapsed) "Show its goals" else "Hide its goals",
                onLongClickLabel = "Edit area",
                onLongClick = onEdit,
                onClick = onToggle,
            )
            .semantics { heading(); stateDescription = if (collapsed) "Hidden" else "Shown" }
            .padding(start = 16.dp, end = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Icon(Icons.Outlined.ExpandMore, contentDescription = null, tint = colors.muted, modifier = Modifier.size(20.dp).rotate(caret))
        Icon(GoalIcons.of(area.icon), contentDescription = null, tint = colors.ink, modifier = Modifier.size(20.dp))
        Text(area.name, style = Ab.type.body.copy(fontSize = 18.sp), fontWeight = FontWeight.Bold, color = colors.ink, maxLines = 1, modifier = Modifier.weight(1f, fill = false))
        Text(
            count.toString(), style = Ab.type.micro.copy(fontFeatureSettings = "tnum"), color = colors.muted,
            modifier = Modifier.clip(Ab.shapes.pill).background(colors.surface2).padding(horizontal = 8.dp, vertical = 2.dp),
        )
        Box(Modifier.weight(1f))
        Box(Modifier.size(48.dp), contentAlignment = Alignment.Center) {
            if (hovered || focused || pencilFocused || pencilHovered) {
                IconButton(onClick = onEdit, interactionSource = pencil, modifier = Modifier.hoverable(pencil)) {
                    Icon(Icons.Outlined.Edit, contentDescription = "Edit area", tint = colors.muted, modifier = Modifier.size(20.dp))
                }
            }
        }
    }
}
