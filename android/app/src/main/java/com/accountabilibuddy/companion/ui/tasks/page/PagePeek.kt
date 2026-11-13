package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.layout
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.ShelfPage

/** The width of a peek, and of the space kept for one on a side that has none (so the card never changes width between pages). */
val PeekWidth = 24.dp

/**
 * A slim peek of the neighbouring page at the card's side (board Tasks-Phone, change note §2): 24 dp wide, three grip lines, the
 * page's marker (the bin's icon in the accent at 15 dp, or a label's 8 dp dot), its name running vertically (12 sp bold muted; the
 * left one reads bottom to top), three grip lines. Neutral at rest whatever the label's colour. A tap shows that page.
 */
@Composable
fun PagePeek(page: ShelfPage, left: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val shape = if (left) RoundedCornerShape(topEnd = 12.dp, bottomEnd = 12.dp) else RoundedCornerShape(topStart = 12.dp, bottomStart = 12.dp)
    Column(
        modifier
            .width(PeekWidth)
            .fillMaxHeight()
            .clip(shape)
            .background(colors.surface)
            .clickable(role = Role.Button, onClick = onClick)
            .semantics { contentDescription = "Show ${page.title}" },
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(10.dp, Alignment.CenterVertically),
    ) {
        GripLines()
        PageMarker(page, iconSize = 15.dp, dotSize = 8.dp)
        VerticalText(
            page.title, bottomToTop = left,
            style = Ab.type.micro.copy(fontSize = 12.sp, fontWeight = FontWeight.Bold, letterSpacing = 0.3.sp, color = colors.muted),
        )
        GripLines()
    }
}

/** Three 10 × 2 dp grip lines at 70%. */
@Composable
fun GripLines(modifier: Modifier = Modifier) {
    val color = Ab.colors.faint.copy(alpha = 0.7f)
    Column(modifier, verticalArrangement = Arrangement.spacedBy(3.dp)) {
        repeat(3) { Box(Modifier.size(width = 10.dp, height = 2.dp).background(color, RoundedCornerShape(2.dp))) }
    }
}

/** One line of text turned a quarter: reading bottom to top ([bottomToTop]) or top to bottom. It takes its length as its height. */
@Composable
fun VerticalText(text: String, bottomToTop: Boolean, style: TextStyle, modifier: Modifier = Modifier) {
    Text(
        text, style = style, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis,
        modifier = modifier
            .layout { measurable, constraints ->
                // Measured as a normal line no longer than the height there is, then laid out with its sides swapped.
                val line = measurable.measure(Constraints(maxWidth = if (constraints.hasBoundedHeight) constraints.maxHeight else Constraints.Infinity))
                layout(line.height, line.width) {
                    line.place(x = (line.height - line.width) / 2, y = (line.width - line.height) / 2)
                }
            }
            .graphicsLayer { rotationZ = if (bottomToTop) -90f else 90f },
    )
}
