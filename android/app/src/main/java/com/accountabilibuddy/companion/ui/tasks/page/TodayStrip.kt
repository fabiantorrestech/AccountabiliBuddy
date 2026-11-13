package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.animation.core.animateDpAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ChevronRight
import androidx.compose.material.icons.outlined.Today
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.LocalMotionScale
import com.accountabilibuddy.companion.ui.motionSpec
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.ShelfDrag

/**
 * The Today strip pinned under the Tasks header (boards Tasks-Phone and Tasks-Phone-Drag, change note §2): a dashed 52 dp row with
 * the calendar icon, "Today", the counts ([counts]: "This session 3 ready · Queued 2 · Reminders 1", never a timer) and ›. A tap
 * opens the Today tab.
 *
 * It is also the drop target into Today › Queued: while a row is lifted ([lit], R7.3b's drag) it grows to 64 dp inside the 64 dp it
 * always reserves (so nothing below moves), takes the accent's dashed border and a tint and says "Drop to add to Today · Queued"
 * ("Or drop to add to…" when [selecting]); with the row over it ([hot]) the tint is stronger.
 */
@Composable
fun TodayStrip(counts: String, onOpen: () -> Unit, modifier: Modifier = Modifier, lit: Boolean = false, hot: Boolean = false, selecting: Boolean = false) {
    val colors = Ab.colors
    // It grows inside its reserved 64 dp; with motion off it just is the new size.
    val height by animateDpAsState(if (lit) 64.dp else 52.dp, motionSpec(LocalMotionScale.current, 160), label = "strip")
    val shape = RoundedCornerShape(18.dp)
    val border = if (hot) colors.accent else if (lit) colors.accent.copy(alpha = 0.55f) else colors.line
    val fill = when {
        hot -> colors.accent.copy(alpha = 0.26f)
        lit -> colors.accent.copy(alpha = 0.10f)
        else -> Color.Transparent
    }
    // 64 dp is always reserved: the strip grows into it while a row is lifted, and the chips below never move (stable layout rule 2).
    Box(modifier.fillMaxWidth().height(64.dp).padding(horizontal = 16.dp), contentAlignment = Alignment.TopCenter) {
        Row(
            Modifier
                .fillMaxWidth()
                .height(height)
                .clip(shape)
                .background(fill)
                .dashedBorder(border, if (lit) 2.dp else 1.5.dp, 18.dp)
                .clickable(role = Role.Button, onClick = onOpen)
                .semantics { contentDescription = "Today. $counts. Opens Today" }
                .padding(start = 16.dp, end = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Icon(Icons.Outlined.Today, contentDescription = null, tint = if (lit) colors.accent else colors.ink, modifier = Modifier.size(22.dp))
            Column(Modifier.weight(1f)) {
                if (lit) {
                    Text(
                        buildAnnotatedString {
                            append(ShelfDrag.stripLead(selecting))
                            withStyle(SpanStyle(fontWeight = FontWeight.ExtraBold)) { append("Today · Queued") }
                        },
                        style = Ab.type.bodyMedium.copy(fontSize = 15.5.sp), fontWeight = FontWeight.Medium, color = colors.accent, maxLines = 1, overflow = TextOverflow.Ellipsis,
                    )
                } else {
                    Text("Today", style = Ab.type.bodyMedium.copy(fontSize = 14.5.sp), fontWeight = FontWeight.SemiBold, color = colors.ink, maxLines = 1)
                    Text(counts, style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
            // The board's lit strip has only its words: the counts and the › come back when the row is let go.
            if (!lit) Icon(Icons.Outlined.ChevronRight, contentDescription = null, tint = colors.muted, modifier = Modifier.size(22.dp))
        }
    }
}

/** A dashed rounded outline ([width] thick, corners [radius]) in [color], as the strip, the "+" chip and the drop zones use it. */
fun Modifier.dashedBorder(color: Color, width: Dp, radius: Dp): Modifier = drawBehind {
    val stroke = width.toPx()
    drawRoundRect(
        color = color,
        topLeft = androidx.compose.ui.geometry.Offset(stroke / 2, stroke / 2),
        size = androidx.compose.ui.geometry.Size(size.width - stroke, size.height - stroke),
        cornerRadius = CornerRadius(radius.toPx()),
        style = Stroke(width = stroke, pathEffect = PathEffect.dashPathEffect(floatArrayOf(5.dp.toPx(), 4.dp.toPx()))),
    )
}
