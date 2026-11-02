package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.background
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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.MoreVert
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.MenuDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbCheck
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * One task in a group of the Tasks pane (board 5v): its number (This session) or a tick box, its title, a muted line with its
 * category (a small coloured dot and the name) and the rest, and a ⋯ menu. A tap on the row opens its details; a tap on the
 * number or the box ticks it ([onTick] null: the mark only decorates). A ticked task is struck through and dimmed. The row
 * is as wide as it is given and as tall as its text.
 */
@Composable
fun TaskRow(row: TaskRowModel, onOpen: () -> Unit, onTick: (() -> Unit)?, menu: List<TaskMenuItem>, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Row(
        modifier.fillMaxWidth().clip(Ab.shapes.mediumShape).let { if (row.isNow) it.background(colors.surface2) else it }
            .clickable(role = Role.Button, onClick = onOpen).heightIn(min = 56.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            Modifier.size(width = 48.dp, height = 56.dp).let {
                if (onTick != null) it.clickable(role = Role.Checkbox, onClick = onTick).semantics { contentDescription = if (row.done) "Ticked, untick" else "Tick" } else it
            },
            contentAlignment = Alignment.Center,
        ) {
            if (row.number != null) {
                Text(row.number.toString(), style = Ab.type.body, color = colors.muted, textAlign = TextAlign.Center)
            } else {
                AbCheck(row.done)
            }
        }
        Column(Modifier.weight(1f).padding(vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(
                row.title, style = Ab.type.body, fontWeight = if (row.isNow) FontWeight.SemiBold else FontWeight.Medium,
                color = if (row.done) colors.muted else colors.ink, textDecoration = if (row.done) TextDecoration.LineThrough else null,
                maxLines = 3, overflow = TextOverflow.Ellipsis,
            )
            CategoryLine(row.category, row.categoryColor, row.line)
        }
        if (menu.isNotEmpty()) {
            TaskRowMenu(menu)
        } else {
            Spacer(Modifier.width(12.dp))
        }
    }
}

/** The muted line under a title: the category as a small coloured dot and its name, then the rest ("2 of 3 steps left · 50 min"). */
@Composable
fun CategoryLine(category: String?, categoryColor: String?, rest: String, modifier: Modifier = Modifier) {
    if (category == null && rest.isEmpty()) return
    // The dot sits at the first line, so a line that wraps (a large font) keeps it beside the category's name.
    Row(modifier, verticalAlignment = Alignment.Top) {
        if (category != null) {
            Box(Modifier.padding(top = 6.dp).size(8.dp).background(parseCategoryColor(categoryColor) ?: Ab.colors.faint, CircleShape))
            Spacer(Modifier.width(6.dp))
        }
        val text = if (category != null && rest.isNotEmpty()) "$category · $rest" else category ?: rest
        Text(text, style = Ab.type.small, color = Ab.colors.muted, maxLines = 2, overflow = TextOverflow.Ellipsis)
    }
}

/** The ⋯ button of a row and its menu; each entry has a Material icon. */
@Composable
fun TaskRowMenu(items: List<TaskMenuItem>, modifier: Modifier = Modifier) {
    var open by remember { mutableStateOf(false) }
    val colors = Ab.colors
    Box(modifier) {
        AbQuietIconButton(Icons.Outlined.MoreVert, "More for this task", onClick = { open = true })
        DropdownMenu(expanded = open, onDismissRequest = { open = false }, containerColor = colors.surface2, shape = Ab.shapes.mediumShape) {
            items.forEach { item ->
                DropdownMenuItem(
                    text = { Text(item.label, style = Ab.type.bodyMedium, color = colors.ink) },
                    leadingIcon = { Icon(item.icon, contentDescription = null, tint = colors.muted, modifier = Modifier.size(20.dp)) },
                    colors = MenuDefaults.itemColors(textColor = colors.ink, leadingIconColor = colors.muted),
                    onClick = { open = false; item.onClick() },
                )
            }
        }
    }
}
