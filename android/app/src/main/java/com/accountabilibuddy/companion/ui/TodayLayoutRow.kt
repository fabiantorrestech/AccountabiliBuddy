package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material.icons.outlined.Visibility
import androidx.compose.material.icons.outlined.VisibilityOff
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.today.TodayItem
import com.accountabilibuddy.companion.today.TodayPlacement
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * One row of the Today layout sheet: the drag [handle], the part, then two 44 dp buttons. The first moves the part into "More today"
 * or back above it (blank where the part can't fold); the second hides it or brings it back, and is a lock where it can't be hidden.
 */
@Composable
fun TodayLayoutRow(part: TodayItem, placement: TodayPlacement, handle: @Composable () -> Unit, onFold: () -> Unit, onHide: () -> Unit) {
    val colors = Ab.colors
    val type = Ab.type
    val hidden = placement == TodayPlacement.Hidden
    Row(
        Modifier.fillMaxWidth().heightIn(min = 60.dp).padding(vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        handle()
        Icon(part.icon(), contentDescription = null, tint = if (hidden) colors.faint else colors.ink, modifier = Modifier.size(22.dp))
        Column(Modifier.weight(1f).padding(start = 8.dp)) {
            Text(part.title, style = type.body, fontWeight = FontWeight.Medium, color = if (hidden) colors.faint else colors.ink)
            Text(part.about, style = type.small, color = if (hidden) colors.faint else colors.muted)
        }
        if (part.canFold && !hidden) {
            val folded = placement == TodayPlacement.Fold
            Box(Modifier.size(44.dp).clickable(role = Role.Button, onClick = onFold), contentAlignment = Alignment.Center) {
                Icon(
                    if (folded) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore,
                    contentDescription = if (folded) "Show ${part.title} above More today" else "Move ${part.title} into More today",
                    tint = colors.muted, modifier = Modifier.size(22.dp),
                )
            }
        } else {
            Box(Modifier.size(44.dp))
        }
        if (part.alwaysShown) {
            Box(Modifier.size(44.dp), contentAlignment = Alignment.Center) {
                Icon(Icons.Outlined.Lock, contentDescription = "${part.title} always shows", tint = colors.faint, modifier = Modifier.size(20.dp))
            }
        } else {
            Box(Modifier.size(44.dp).clickable(role = Role.Switch, onClick = onHide), contentAlignment = Alignment.Center) {
                Icon(
                    if (hidden) Icons.Outlined.VisibilityOff else Icons.Outlined.Visibility,
                    contentDescription = if (hidden) "Show ${part.title}" else "Hide ${part.title}", tint = colors.muted, modifier = Modifier.size(22.dp),
                )
            }
        }
    }
}
