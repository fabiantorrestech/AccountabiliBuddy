package com.accountabilibuddy.companion.ui.tasks

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * "Come back to" (ADR 30, 48): the user's parked thoughts, one list on every device, each with the category it carries. Tick one,
 * or use its ⋯ menu: Add to Today, Edit, Move to Later, Delete. Changes are in this phone's copy at once and reach the PCs with the
 * next sync, where the newest edit of each thought wins.
 *
 * Next to a PC that predates the shared list ([oldPc], ADR 48) the card shows that PC's own open come-backs: they can be
 * ticked, which goes to it as a sync action (ADR 26), and nothing else, and it says to update that PC.
 *
 * @param filtering A chip or the search is on (an empty list then reads "Nothing matches.").
 */
@Composable
fun ComebacksCard(rows: List<TaskRowModel>, oldPc: String?, filtering: Boolean, pc: TasksPc?, actions: TasksPaneActions) {
    AbCard(contentPadding = PaddingValues(horizontal = 10.dp, vertical = 14.dp), verticalSpacing = 4.dp) {
        AbSectionLabel(if (oldPc == null) "Come back to" else "Come back to · $oldPc", modifier = Modifier.padding(horizontal = 10.dp))
        if (oldPc != null) {
            Text(
                "Update AccountabiliBuddy on $oldPc to edit come-backs here.", style = Ab.type.small, color = Ab.colors.muted,
                modifier = Modifier.padding(horizontal = 10.dp),
            )
        }
        if (rows.isEmpty()) {
            EmptyLine(if (filtering) "Nothing matches." else "Nothing parked.")
        } else {
            TaskRows(rows, actions, pc, reorder = false)
        }
    }
}
