package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.KeyboardArrowDown
import androidx.compose.material.icons.outlined.KeyboardArrowUp
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * "Later · 11 · Next due: …" (ADR 50), collapsed by default: the things the user keeps for another day, edited here as on the
 * PC (add, rename, tick, steps, estimate, note, category, due date, delete, move to This session or Come back to). Open, it shows
 * its rows and a field to add to Later. A ticked Later item leaves the list and shows under Done today on this phone.
 *
 * @param total How many Later items there are (the header's count, whatever a chip or the search shows).
 * @param nextDue "Next due: Expense report, Fri", null when no item has a date.
 * @param pc The PC with a running session, or null (Add to Today then goes to its checklist).
 * @param filtering A chip or the search is on.
 */
@Composable
fun LaterGroup(
    rows: List<TaskRowModel>,
    total: Int,
    nextDue: String?,
    expanded: Boolean,
    onToggle: () -> Unit,
    pc: TasksPc?,
    filtering: Boolean,
    actions: TasksPaneActions,
    onAdd: (String) -> Unit,
) {
    AbCard(contentPadding = PaddingValues(horizontal = 10.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
        FoldHeader("Later · ${if (filtering) "${rows.size} of $total" else total.toString()}", nextDue, expanded, onToggle)
        if (expanded) {
            if (rows.isEmpty()) EmptyLine(if (filtering) "Nothing matches." else "Nothing kept for later.") else TaskRows(rows, actions, pc, reorder = false)
            AddField("Add to Later", onSubmit = onAdd, modifier = Modifier.padding(top = 6.dp, bottom = 10.dp))
        }
    }
}

/**
 * "Done today · 4", collapsed by default: what was ticked today (the running session's, the next session's and the Later items
 * ticked on this phone). A ticked task of this phone's lists can be put back from its ⋯ menu.
 */
@Composable
fun DoneTodayGroup(rows: List<TaskRowModel>, expanded: Boolean, onToggle: () -> Unit, actions: TasksPaneActions) {
    AbCard(contentPadding = PaddingValues(horizontal = 10.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
        FoldHeader("Done today · ${rows.size}", null, expanded, onToggle)
        if (expanded) TaskRows(rows, actions, pc = null, reorder = false, modifier = Modifier.padding(bottom = 6.dp))
    }
}

/** The header of a folded group: a chevron, the title and, at the end, a muted note; the whole line is the button. */
@Composable
internal fun FoldHeader(title: String, note: String?, expanded: Boolean, onToggle: () -> Unit) {
    val colors = Ab.colors
    Row(
        Modifier.fillMaxWidth().heightIn(min = 56.dp).clickable(role = Role.Button, onClick = onToggle).padding(horizontal = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Icon(
            if (expanded) Icons.Outlined.KeyboardArrowUp else Icons.Outlined.KeyboardArrowDown,
            contentDescription = if (expanded) "Collapse" else "Expand", tint = colors.muted, modifier = Modifier.size(22.dp),
        )
        Text(title, style = Ab.type.body, fontWeight = FontWeight.Medium, color = colors.ink, maxLines = 1)
        if (note != null) {
            Text(note, style = Ab.type.small, color = colors.muted, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f), textAlign = androidx.compose.ui.text.style.TextAlign.End)
        }
    }
}
