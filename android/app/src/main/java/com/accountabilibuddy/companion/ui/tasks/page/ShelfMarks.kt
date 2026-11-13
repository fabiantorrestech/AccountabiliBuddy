package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.FormatListBulleted
import androidx.compose.material.icons.outlined.Inbox
import androidx.compose.material.icons.outlined.MoveToInbox
import androidx.compose.material.icons.outlined.Start
import androidx.compose.material.icons.outlined.Tag
import androidx.compose.material3.Icon
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.tasks.parseCategoryColor
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.ShelfPage
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskLabelColors

/** The bins' icons (brief §3.10, ADR 32; the same on the PC): To sort an inbox tray, Next an arrow to a bar, Later an arrow into a tray. */
fun binIcon(bin: TaskBin?): ImageVector = when (bin) {
    TaskBin.SORT -> Icons.Outlined.Inbox
    TaskBin.NEXT -> Icons.Outlined.Start
    TaskBin.LATER -> Icons.Outlined.MoveToInbox
    else -> Icons.AutoMirrored.Outlined.FormatListBulleted
}

/** A label's colour as the theme can draw it; null when it has none or it can't be read. */
fun labelColor(color: String?): Color? = color?.let { parseCategoryColor(TaskLabelColors.opaque(it)) }

/** A label's colour dot; a hollow ring for "No label" ([color] null). Label colour is only ever a dot at rest (change note §2). */
@Composable
fun LabelDot(color: String?, size: Dp, modifier: Modifier = Modifier) {
    val fill = labelColor(color)
    if (fill != null) {
        Box(modifier.size(size).background(fill, CircleShape))
    } else {
        Box(modifier.size(size).border(1.5.dp, Ab.colors.faint, CircleShape))
    }
}

/** A page's marker: the bin's icon in the accent, a label's dot, or a tag sign; [iconSize] and [dotSize] as the place draws them. */
@Composable
fun PageMarker(page: ShelfPage, iconSize: Dp, dotSize: Dp, modifier: Modifier = Modifier) {
    val bin = page.bin
    when {
        bin != null -> Icon(binIcon(bin), contentDescription = null, tint = Ab.colors.accent, modifier = modifier.size(iconSize))
        page.tag != null || page.key.startsWith("tag:") -> Icon(Icons.Outlined.Tag, contentDescription = null, tint = Ab.colors.muted, modifier = modifier.size(iconSize))
        page.key == "all" -> Icon(binIcon(null), contentDescription = null, tint = Ab.colors.accent, modifier = modifier.size(iconSize))
        else -> LabelDot(page.labelColor, dotSize, modifier)
    }
}
