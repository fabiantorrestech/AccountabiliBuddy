package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Tag
import androidx.compose.material3.Icon
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.ShelfDrag
import com.accountabilibuddy.shared.tasks.ShelfPage

/**
 * A drop zone over the card's left or right edge while a row is lifted (boards Tasks-Phone-Drag, -Drag-Bin, -Label-Drag; change
 * note §2): 58 dp wide, a 2 dp dashed outline, the neighbouring page's marker and its name running vertically (14 sp / 800; the
 * left one reads bottom to top). It lies over the peek and the card's edge: the card keeps its size and nothing under it moves.
 *
 * A bin's zone stays on the accent. A label's zone takes a subtle tint of the label's colour (16%, 30% while the row is over it
 * ([hot])), its outline and words in that colour and a 12 dp dot: colour shows only while choosing where to drop.
 */
@Composable
fun DropZone(page: ShelfPage, left: Boolean, hot: Boolean, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val label = labelColor(page.labelColor)
    val shape = RoundedCornerShape(20.dp)
    val fill = if (label != null) lerp(colors.bg, label, if (hot) 0.30f else 0.16f) else lerp(colors.bg, colors.accent, if (hot) 0.22f else 0.08f)
    val outline = if (label != null) lerp(colors.surface, label, if (hot) 0.85f else 0.55f) else colors.accent.copy(alpha = if (hot) 1f else 0.55f)
    val ink = if (label != null) lerp(colors.ink, label, 0.70f) else colors.accent
    Column(
        modifier
            .width(ShelfDrag.ZONE_DP.dp)
            .fillMaxHeight()
            .shadow(12.dp, shape)
            .clip(shape)
            .background(fill)
            .dashedBorder(outline, 2.dp, 20.dp)
            .semantics { contentDescription = "Drop into ${page.title}" },
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(10.dp, Alignment.CenterVertically),
    ) {
        val bin = page.bin
        when {
            bin != null -> Icon(binIcon(bin), contentDescription = null, tint = ink, modifier = Modifier.size(20.dp))
            page.tag != null -> Icon(Icons.Outlined.Tag, contentDescription = null, tint = ink, modifier = Modifier.size(20.dp))
            label != null -> androidx.compose.foundation.layout.Box(Modifier.size(12.dp).background(label, CircleShape))
            else -> LabelDot(null, 12.dp)
        }
        VerticalText(page.title, bottomToTop = left, style = Ab.type.bodyMedium.copy(fontSize = 14.sp, fontWeight = FontWeight.ExtraBold, color = ink))
    }
}
