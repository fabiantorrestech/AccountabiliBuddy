package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskBins

/** A sheet's section head (board Tasks-Phone-Options): "ARRANGE", 12.5 sp bold muted. */
@Composable
fun SheetSectionHead(text: String) {
    Text(
        text.uppercase(), style = Ab.type.micro.copy(fontSize = 12.5.sp), fontWeight = FontWeight.Bold, color = Ab.colors.muted,
        modifier = Modifier.padding(start = 24.dp, end = 24.dp, top = 14.dp, bottom = 4.dp).semantics { heading() },
    )
}

/** A muted note at the foot of a sheet page. */
@Composable
fun SheetFoot(text: String) {
    Text(text, style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 10.dp))
}

/**
 * A sheet row whose leading mark is drawn by [leading] in the icon's 22 dp place (a label's colour dot): otherwise the same as
 * [com.accountabilibuddy.companion.ui.sheets.AbSheetRow] (58 dp, 15.5 sp title, a trailing side).
 */
@Composable
fun SheetMarkRow(title: String, onClick: () -> Unit, leading: @Composable () -> Unit, trailing: (@Composable () -> Unit)? = null) {
    Row(
        Modifier.fillMaxWidth().heightIn(min = 58.dp).clickable(role = Role.Button, onClick = onClick).padding(horizontal = 24.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Box(Modifier.size(22.dp), contentAlignment = Alignment.Center) { leading() }
        Text(title, style = Ab.type.bodyMedium.copy(fontSize = 15.5.sp), color = Ab.colors.ink, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
        trailing?.invoke()
    }
}

/**
 * The "Move to" grid of the ⋮ task sheet and the Add sheet's "Send to" (boards Tasks-Phone-Task-Menu and -Add): the heads "Today"
 * [This session] [Queued] and "Shelf" [To sort] [Next] [Later], 40 dp tiles, the [current] one filled with the accent.
 */
@Composable
fun MoveToGrid(label: String, current: TaskBin?, onPick: (TaskBin) -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Column(modifier.fillMaxWidth().padding(start = 24.dp, end = 24.dp, top = 4.dp, bottom = 10.dp)) {
        Text(label, style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted, modifier = Modifier.padding(bottom = 6.dp))
        Text("Today", style = Ab.type.micro.copy(fontSize = 12.sp, letterSpacing = 0.sp), color = colors.faint, modifier = Modifier.padding(top = 2.dp, bottom = 4.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            BinTile(TaskBin.SESSION, current, onPick)
            BinTile(TaskBin.QUEUED, current, onPick)
        }
        Text("Shelf", style = Ab.type.micro.copy(fontSize = 12.sp, letterSpacing = 0.sp), color = colors.faint, modifier = Modifier.padding(top = 8.dp, bottom = 4.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            TaskBins.SHELF.forEach { BinTile(it, current, onPick) }
        }
    }
}

@Composable
private fun RowScope.BinTile(bin: TaskBin, current: TaskBin?, onPick: (TaskBin) -> Unit) {
    val colors = Ab.colors
    val chosen = bin == current
    val shape = RoundedCornerShape(12.dp)
    Box(
        // 40 dp as drawn, inside a 48 dp touch target.
        Modifier.weight(1f).heightIn(min = 48.dp).clip(shape).clickable(role = Role.RadioButton) { onPick(bin) }.semantics { selected = chosen },
        contentAlignment = Alignment.Center,
    ) {
        Box(Modifier.fillMaxWidth().height(40.dp).background(if (chosen) colors.accent else colors.surface2, shape), contentAlignment = Alignment.Center) {
            Text(
                TaskBins.name(bin), style = Ab.type.small.copy(fontSize = 13.5.sp), fontWeight = FontWeight.SemiBold,
                color = if (chosen) colors.onAccent else colors.ink, maxLines = 1,
            )
        }
    }
}

/** Fades what a sheet can't do yet to 40%, as the boards draw "Coming soon" rows. */
fun Modifier.comingSoon(): Modifier = alpha(0.4f)
