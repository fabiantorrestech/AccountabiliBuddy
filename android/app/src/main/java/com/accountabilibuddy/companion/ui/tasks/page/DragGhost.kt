package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.ShelfDrag

/**
 * The card that follows the finger while a row is lifted (boards Tasks-Phone-Drag and -Drag-Bin; change note §2): about 250 dp
 * wide, tilted −2°, with the task's title and the same label dot and detail line as its row ([line]). For several tasks
 * ([count] > 1) it is a loose stack: up to two cards behind it, offset and tilted the other ways (+4°, −5°), and a count badge at
 * its corner. Over a drop target it shrinks a little ([over]). It is decoration: a screen reader never sees it.
 */
@Composable
fun DragGhost(title: String, line: String, dotColor: String?, showDot: Boolean, count: Int, over: Boolean, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val shape = RoundedCornerShape(16.dp)
    val scale = if (over) 0.9f else 1f
    Box(modifier.width(ShelfDrag.GHOST_WIDTH_DP.dp).graphicsLayer { scaleX = scale; scaleY = scale }.clearAndSetSemantics { }) {
        val behind = (count - 1).coerceIn(0, ShelfDrag.MAX_BACK_CARDS)
        if (behind >= 2) {
            Box(Modifier.matchParentSize().offset(x = (-6).dp, y = 9.dp).graphicsLayer { rotationZ = -5f }.shadow(6.dp, shape).background(colors.surface2, shape).border(1.dp, colors.line, shape))
        }
        if (behind >= 1) {
            Box(Modifier.matchParentSize().offset(x = 7.dp, y = 7.dp).graphicsLayer { rotationZ = 4f }.shadow(8.dp, shape).background(colors.surface2, shape).border(1.dp, colors.line, shape))
        }
        Column(
            Modifier.fillMaxWidth().graphicsLayer { rotationZ = -2f }.shadow(16.dp, shape).background(colors.surface2, shape).border(1.dp, colors.line, shape)
                .padding(horizontal = 14.dp, vertical = 10.dp),
        ) {
            Text(title, style = Ab.type.body.copy(fontSize = 15.sp), fontWeight = FontWeight.Medium, color = colors.ink, maxLines = 2, overflow = TextOverflow.Ellipsis)
            if (line.isNotEmpty() || showDot) {
                Row(Modifier.padding(top = 2.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    if (showDot) LabelDot(dotColor, 8.dp)
                    Text(line, style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
        }
        if (count > 1) {
            Box(
                Modifier.align(Alignment.TopEnd).offset(x = 10.dp, y = (-10).dp).defaultMinSize(minWidth = 26.dp, minHeight = 26.dp).background(colors.accent, CircleShape).padding(horizontal = 6.dp),
                contentAlignment = Alignment.Center,
            ) {
                Text(count.toString(), style = Ab.type.small.copy(fontSize = 13.sp, fontFeatureSettings = "tnum"), fontWeight = FontWeight.ExtraBold, color = colors.onAccent)
            }
        }
    }
}
