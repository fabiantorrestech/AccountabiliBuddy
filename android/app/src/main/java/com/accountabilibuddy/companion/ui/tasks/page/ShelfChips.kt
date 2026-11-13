package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskBins
import com.accountabilibuddy.shared.tasks.TaskLabel

/** The height the chip row always takes (the 38 dp chips and the board's 10 dp under them), also when the search field is in its place. */
val ChipRowHeight = 48.dp

/**
 * The chip row under the Today strip (boards Tasks-Phone and Tasks-Phone-Label). Grouped by bins it filters by label: a dashed round
 * "+" (new label), All, one 38 dp chip per label. Grouped by label or tag the pages are the labels, so the chips are the bins: the
 * caption "Grouped by label", then All bins, To sort, Next, Later. Group by None shows the label chips too.
 */
@Composable
fun ShelfChips(
    byBins: Boolean,
    caption: String,
    labels: List<TaskLabel>,
    chipLabel: String?,
    onChipLabel: (String?) -> Unit,
    chipBin: TaskBin?,
    onChipBin: (TaskBin?) -> Unit,
    onNewLabel: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = Ab.colors
    Row(
        modifier.fillMaxWidth().height(ChipRowHeight).horizontalScroll(rememberScrollState()).padding(start = 16.dp, end = 16.dp, bottom = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        if (byBins) {
            Box(
                Modifier.size(38.dp).clip(CircleShape).dashedBorder(colors.line, 1.dp, 19.dp).clickable(role = Role.Button, onClick = onNewLabel)
                    .semantics { contentDescription = "New label" },
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Outlined.Add, contentDescription = null, tint = colors.ink, modifier = Modifier.size(18.dp))
            }
            ShelfChip("All", chipLabel == null) { onChipLabel(null) }
            labels.forEach { label -> ShelfChip(label.name, chipLabel.equals(label.id, ignoreCase = true)) { onChipLabel(label.id) } }
        } else {
            Text(caption, style = Ab.type.small, color = colors.muted, maxLines = 1)
            ShelfChip("All bins", chipBin == null) { onChipBin(null) }
            TaskBins.SHELF.forEach { bin -> ShelfChip(TaskBins.name(bin), chipBin == bin) { onChipBin(bin) } }
        }
    }
}

/** One 38 dp chip: a 1 dp line border; the chosen one filled with surface2 and bordered in ink. */
@Composable
fun ShelfChip(text: String, selected: Boolean, modifier: Modifier = Modifier, height: Int = 38, onClick: () -> Unit) {
    val colors = Ab.colors
    Surface(
        onClick = onClick,
        modifier = modifier.height(height.dp).semantics { this.selected = selected; role = Role.Tab },
        shape = CircleShape,
        color = if (selected) colors.surface2 else Color.Transparent,
        contentColor = colors.ink,
        border = BorderStroke(1.dp, if (selected) colors.ink else colors.line),
    ) {
        Box(Modifier.padding(horizontal = 14.dp), contentAlignment = Alignment.Center) {
            Text(text, style = Ab.type.bodyMedium.copy(fontSize = 14.sp), fontWeight = FontWeight.Medium, maxLines = 1)
        }
    }
}
